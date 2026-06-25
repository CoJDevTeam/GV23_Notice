using GV23_Notice.Data;
using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.QA;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.Storage;
using GV23_Notice.Services.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Net.Mail;
using System.Text;

namespace GV23_Notice.Services.Email
{
    public sealed class NoticeBatchEmailService : INoticeBatchEmailService
    {
        private readonly AppDbContext _db;
        private readonly EmailOptions _emailOpt;
        private readonly INoticeEmailTemplateService _templates;
        private readonly INoticePathService _paths;
        private readonly IS49RollRepository _s49Repo;
        private readonly IConfiguration _config;
        private readonly ILogger<NoticeBatchEmailService> _log;
        private readonly INoticeQaService _qa;
        private readonly INoticeSourceStatusService _sourceStatus;
        private const int HardMaxEmails = 2000;

        public NoticeBatchEmailService(
            AppDbContext db,
            IOptions<EmailOptions> emailOpt,
            INoticeEmailTemplateService templates,
            INoticePathService paths,
            IS49RollRepository s49Repo,
            IConfiguration config,
            ILogger<NoticeBatchEmailService> log,INoticeQaService qa, INoticeSourceStatusService sourceStatus)
        {
            _db = db;
            _emailOpt = emailOpt.Value;
            _templates = templates;
            _paths = paths;
            _s49Repo = s49Repo;
            _config = config;
            _log = log;
            _qa = qa;
            _sourceStatus = sourceStatus;
        }

        // ── Count ready-to-send records ──────────────────────────────────────
        public async Task<int> CountSelectedRecordsAsync(
     IEnumerable<int> batchIds, CancellationToken ct)
        {
            var ids = batchIds.Distinct().ToList();

            if (ids.Count == 0)
                return 0;

            return await _db.NoticeRunLogs
                .Where(r => ids.Contains(r.NoticeBatchId)
                         && r.Status == RunStatus.Printed
                         && r.RecipientEmail != null
                         && r.RecipientEmail != ""
                         && r.PdfPath != null
                         && r.PdfPath != "")
                .CountAsync(ct);
        }

        // ── Send emails for selected batches ─────────────────────────────────
        public async Task<SendBatchEmailResult> SendBatchEmailsAsync(
     IEnumerable<int> batchIds,
     Guid workflowKey,
     string sentBy,
     CancellationToken ct)
        {
            var ids = batchIds.Distinct().ToList();

            if (ids.Count == 0)
                return new SendBatchEmailResult { ErrorMessage = "No batches selected." };

            if (!await _qa.IsQaApprovedAsync(workflowKey, ct))
            {
                return new SendBatchEmailResult
                {
                    ErrorMessage = "QA must be approved before notices can be sent."
                };
            }

            var settings = await _db.NoticeSettings.AsNoTracking()
                               .FirstOrDefaultAsync(s => s.ApprovalKey == workflowKey
                                                      || s.WorkflowKey == workflowKey, ct)
                           ?? throw new InvalidOperationException("Workflow settings not found.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == settings.RollId, ct)
                       ?? throw new InvalidOperationException("Roll not found.");

            var batches = await _db.NoticeBatches.AsNoTracking()
                .Where(b => ids.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct);

            var runLogs = await _db.NoticeRunLogs
                .Where(r => ids.Contains(r.NoticeBatchId)
                         && r.Status == RunStatus.Printed
                         && r.RecipientEmail != null
                         && r.RecipientEmail != ""
                         && r.PdfPath != null
                         && r.PdfPath != "")
                .OrderBy(r => r.NoticeBatchId)
                .ThenBy(r => r.Id)
                .ToListAsync(ct);

            var result = new SendBatchEmailResult
            {
                BatchesProcessed = ids.Count
            };

            var maxSend = Math.Min(
                HardMaxEmails,
                _emailOpt.Limits?.MaxSendPerBatch ?? HardMaxEmails);

            if (runLogs.Count > maxSend)
            {
                result.ErrorMessage =
                    $"Selection contains {runLogs.Count} emails but the limit is {maxSend}. Please select fewer batches.";

                return result;
            }

            var delay = Math.Max(0, _emailOpt.Limits?.DelayMsBetweenSends ?? 0);

            // ============================================================
            // S53 SPECIAL RULE:
            // Owner_Rep / Owner_Third_Party are companion copies.
            // They share one Obj_Property_Info source status with the
            // Representative / Third_Party record.
            //
            // So send all records for the same ObjectionNo first,
            // then update source status once:
            // Email-Sent-Pending -> Notice-Sent
            // ============================================================
            if (settings.Notice.IsSection53Family())
            {
                var groups = runLogs
                    .Where(x => !string.IsNullOrWhiteSpace(x.ObjectionNo))
                    .GroupBy(x => x.ObjectionNo!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key)
                    .ToList();

                foreach (var group in groups)
                {
                    ct.ThrowIfCancellationRequested();

                    var objectionNo = group.Key;
                    var groupLogs = group.OrderBy(x => x.Id).ToList();

                    batches.TryGetValue(groupLogs.First().NoticeBatchId, out var batch);
                    var batchName = batch?.BatchName ?? "Batch";
                    var rollId = batch?.RollId ?? settings.RollId;

                    try
                    {
                        var canSend = await _sourceStatus.IsS53StatusAsync(
                            rollId,
                            objectionNo,
                            NoticeWorkflowStatus.EmailSentPending,
                            ct);

                        if (!canSend)
                        {
                            throw new InvalidOperationException(
                                $"Cannot send S53 notice {objectionNo}. Source status must be '{NoticeWorkflowStatus.EmailSentPending}'.");
                        }

                        foreach (var log in groupLogs)
                        {
                            try
                            {
                                var objectorType = string.IsNullOrWhiteSpace(log.RecipientName)
                                    ? "Owner"
                                    : log.RecipientName.Trim();

                                var mvdEmailData = await FetchS53MvdEmailDataAsync(
    settings.Notice,
    rollId,
    log.ObjectionNo!,
    batchName,
    objectorType,
    ct);

                                if (mvdEmailData == null)
                                {
                                    throw new InvalidOperationException(
                                        $"S53_GetMvdRow returned no row for ObjectionNo={log.ObjectionNo}, BatchName={batchName}, ObjectorType={objectorType}.");
                                }

                                if (string.IsNullOrWhiteSpace(mvdEmailData.Email))
                                {
                                    throw new InvalidOperationException(
                                        $"S53 email not found in Objection_MVD for ObjectionNo={log.ObjectionNo}, ObjectorType={objectorType}, BatchName={batchName}.");
                                }

                                var recipientEmail = mvdEmailData.Email.Trim();
                                log.RecipientEmail = recipientEmail;

                                var req = BuildEmailRequest(settings, roll, log);
                                req.RecipientEmail = recipientEmail;

                                if (req.Items.Count > 0)
                                {
                                    req.Items[0].PropertyDesc = mvdEmailData.PropertyDesc
                                                               ?? log.PropertyDesc
                                                               ?? "";

                                    req.Items[0].ObjectionNo = mvdEmailData.ObjectionNo
                                                               ?? log.ObjectionNo;
                                }

                                var (subject, bodyHtml) = _templates.Build(req);

                                if (string.IsNullOrWhiteSpace(log.PdfPath))
                                    throw new InvalidOperationException($"PDF path is empty for RunLog {log.Id}.");

                                var emlPath = BuildEmlPathNextToPdf(
                                    pdfPath: log.PdfPath,
                                    notice: settings.Notice,
                                    objectionNo: log.ObjectionNo,
                                    appealNo: log.AppealNo,
                                    propertyDesc: log.PropertyDesc,
                                    recipientEmail: recipientEmail);

                                emlPath = await SaveEmlAsync(
                                    emlPath,
                                    recipientEmail,
                                    subject,
                                    bodyHtml,
                                    log.PdfPath!,
                                    ct,
                                    ccAddress: string.IsNullOrWhiteSpace(_emailOpt.CcAddress)
                                        ? null
                                        : _emailOpt.CcAddress.Trim(),
                                    fromAddress: string.IsNullOrWhiteSpace(_emailOpt.FromAddress)
                                        ? null
                                        : _emailOpt.FromAddress.Trim(),
                                    fromName: string.IsNullOrWhiteSpace(_emailOpt.FromName)
                                        ? null
                                        : _emailOpt.FromName.Trim());

                                log.EmlPath = emlPath;

                                try
                                {
                                    var logForSend = new NoticeRunLog
                                    {
                                        RecipientEmail = recipientEmail,
                                        PdfPath = log.PdfPath
                                    };

                                    await SendOneEmailAsync(subject, bodyHtml, logForSend, ct);

                                    _log.LogInformation(
                                        "✓ SMTP sent to {Email} ({Notice})",
                                        recipientEmail,
                                        settings.Notice);
                                }
                                catch (SmtpFailedRecipientException smtpEx)
                                {
                                    _log.LogWarning(
                                        "⚠ SMTP relay rejected {Email} ({Notice}) — .eml saved. Error: {Msg}",
                                        recipientEmail,
                                        settings.Notice,
                                        smtpEx.Message);
                                }
                                catch (SmtpException smtpEx)
                                {
                                    _log.LogWarning(
                                        "⚠ SMTP error for {Email} ({Notice}) — .eml saved. Error: {Msg}",
                                        recipientEmail,
                                        settings.Notice,
                                        smtpEx.Message);
                                }

                                log.Status = RunStatus.Sent;
                                log.SentAtUtc = DateTime.UtcNow;
                                log.SentBy = sentBy;
                                log.ErrorMessage = null;

                                result.Sent++;

                                await _db.SaveChangesAsync(ct);

                                if (delay > 0)
                                    await Task.Delay(delay, ct);
                            }
                            catch (Exception ex)
                            {
                                _log.LogError(
                                    ex,
                                    "Email send failed for RunLog {Id}. Notice={Notice}, ObjectionNo={ObjectionNo}, ObjectorType={ObjectorType}",
                                    log.Id,
                                    settings.Notice,
                                    log.ObjectionNo,
                                    log.RecipientName);

                                log.Status = RunStatus.Failed;
                                log.ErrorMessage = ex.Message.Length > 2000
                                    ? ex.Message[..2000]
                                    : ex.Message;

                                log.SentBy = sentBy;
                                result.Failed++;

                                await _db.SaveChangesAsync(ct);
                            }
                        }

                        var allGroupSent = groupLogs.All(x => x.Status == RunStatus.Sent);

                        if (allGroupSent)
                        {
                            await _sourceStatus.SetS53StatusAsync(
                                rollId,
                                new[] { objectionNo },
                                NoticeWorkflowStatus.NoticeSent,
                                ct);
                        }

                        await _db.SaveChangesAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(
                            ex,
                            "S53 email group failed. ObjectionNo={ObjectionNo}, Batch={BatchName}",
                            objectionNo,
                            batchName);

                        foreach (var log in groupLogs)
                        {
                            if (log.Status != RunStatus.Sent)
                            {
                                log.Status = RunStatus.Failed;
                                log.ErrorMessage = ex.Message.Length > 2000
                                    ? ex.Message[..2000]
                                    : ex.Message;

                                log.SentBy = sentBy;
                            }
                        }

                        result.Failed += groupLogs.Count(x => x.Status == RunStatus.Failed);

                        await _db.SaveChangesAsync(ct);
                    }
                }

                var missingObjectionLogs = runLogs
                    .Where(x => string.IsNullOrWhiteSpace(x.ObjectionNo))
                    .ToList();

                foreach (var log in missingObjectionLogs)
                {
                    log.Status = RunStatus.Failed;
                    log.ErrorMessage = $"S53 RunLog {log.Id} has no ObjectionNo.";
                    log.SentBy = sentBy;
                    result.Failed++;
                }

                if (missingObjectionLogs.Count > 0)
                    await _db.SaveChangesAsync(ct);

                return result;
            }

            // ============================================================
            // NORMAL SEND LOGIC FOR NON-S53 NOTICES
            // ============================================================
            foreach (var log in runLogs)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    batches.TryGetValue(log.NoticeBatchId, out var batch);
                    var batchName = batch?.BatchName ?? "Batch";
                    var rollId = batch?.RollId ?? settings.RollId;

                    string recipientEmail = log.RecipientEmail ?? "";

                    var freshEmail = await FetchFreshEmailAsync(settings, log, batchName, ct);

                    if (!string.IsNullOrWhiteSpace(freshEmail))
                    {
                        recipientEmail = freshEmail.Trim();
                        log.RecipientEmail = recipientEmail;
                    }

                    if (string.IsNullOrWhiteSpace(recipientEmail))
                        throw new InvalidOperationException($"Recipient email is empty for RunLog {log.Id}.");

                    var req = BuildEmailRequest(settings, roll, log);
                    req.RecipientEmail = recipientEmail;

                    var (subject, bodyHtml) = _templates.Build(req);

                    string emlPath;

                    if (!string.IsNullOrWhiteSpace(log.PdfPath))
                    {
                        emlPath = BuildEmlPathNextToPdf(
                            pdfPath: log.PdfPath,
                            notice: settings.Notice,
                            objectionNo: log.ObjectionNo,
                            appealNo: log.AppealNo,
                            propertyDesc: log.PropertyDesc,
                            recipientEmail: recipientEmail);
                    }
                    else
                    {
                        var folderKey = log.PropertyDesc
                                        ?? log.ObjectionNo
                                        ?? log.AppealNo
                                        ?? log.PremiseId
                                        ?? log.Id.ToString();

                        var oldPath = _paths.BuildBatchEmlPath(
                            roll,
                            settings.Notice,
                            batchName,
                            folderKey);

                        var folder = Path.GetDirectoryName(oldPath)
                                     ?? throw new InvalidOperationException("Could not resolve EML folder.");

                        Directory.CreateDirectory(folder);

                        var fileName = BuildSentNoticeEmlFileName(
                            settings.Notice,
                            log.ObjectionNo,
                            log.AppealNo,
                            log.PropertyDesc,
                            recipientEmail);

                        emlPath = Path.Combine(folder, fileName);
                    }

                    emlPath = await SaveEmlAsync(
                        emlPath,
                        recipientEmail,
                        subject,
                        bodyHtml,
                        log.PdfPath!,
                        ct,
                        ccAddress: string.IsNullOrWhiteSpace(_emailOpt.CcAddress)
                            ? null
                            : _emailOpt.CcAddress.Trim(),
                        fromAddress: string.IsNullOrWhiteSpace(_emailOpt.FromAddress)
                            ? null
                            : _emailOpt.FromAddress.Trim(),
                        fromName: string.IsNullOrWhiteSpace(_emailOpt.FromName)
                            ? null
                            : _emailOpt.FromName.Trim());

                    log.EmlPath = emlPath;

                    try
                    {
                        var logForSend = new NoticeRunLog
                        {
                            RecipientEmail = recipientEmail,
                            PdfPath = log.PdfPath
                        };

                        await SendOneEmailAsync(subject, bodyHtml, logForSend, ct);

                        _log.LogInformation(
                            "✓ SMTP sent to {Email} ({Notice})",
                            recipientEmail,
                            settings.Notice);
                    }
                    catch (SmtpFailedRecipientException smtpEx)
                    {
                        _log.LogWarning(
                            "⚠ SMTP relay rejected {Email} ({Notice}) — .eml saved. Error: {Msg}",
                            recipientEmail,
                            settings.Notice,
                            smtpEx.Message);
                    }
                    catch (SmtpException smtpEx)
                    {
                        _log.LogWarning(
                            "⚠ SMTP error for {Email} ({Notice}) — .eml saved. Error: {Msg}",
                            recipientEmail,
                            settings.Notice,
                            smtpEx.Message);
                    }

                    log.Status = RunStatus.Sent;
                    log.SentAtUtc = DateTime.UtcNow;
                    log.SentBy = sentBy;
                    log.ErrorMessage = null;

                    if (settings.Notice == NoticeKind.S49 && !string.IsNullOrWhiteSpace(log.PremiseId))
                    {
                        await _s49Repo.MarkEmailSentAsync(
                            roll.RollId,
                            log.PremiseId,
                            ct);
                    }

                    result.Sent++;
                }
                catch (Exception ex)
                {
                    _log.LogError(
                        ex,
                        "Email send failed for RunLog {Id}. Notice={Notice}, ObjectionNo={ObjectionNo}, AppealNo={AppealNo}, PremiseId={PremiseId}",
                        log.Id,
                        settings.Notice,
                        log.ObjectionNo,
                        log.AppealNo,
                        log.PremiseId);

                    log.Status = RunStatus.Failed;
                    log.ErrorMessage = ex.Message.Length > 2000
                        ? ex.Message[..2000]
                        : ex.Message;

                    log.SentBy = sentBy;

                    result.Failed++;
                }

                await _db.SaveChangesAsync(ct);

                if (delay > 0)
                    await Task.Delay(delay, ct);
            }

            var noEmailLogs = await _db.NoticeRunLogs
                .Where(r => ids.Contains(r.NoticeBatchId)
                         && r.Status == RunStatus.Printed
                         && (r.RecipientEmail == null || r.RecipientEmail == ""))
                .ToListAsync(ct);

            foreach (var log in noEmailLogs)
            {
                log.Status = RunStatus.NoEmail;
                log.SentBy = sentBy;
                log.ErrorMessage = "Recipient email is empty.";
                result.Skipped++;
            }

            if (noEmailLogs.Count > 0)
                await _db.SaveChangesAsync(ct);

            return result;
        }

        // ── Build NoticeEmailRequest from settings + run log ─────────────────
        private static NoticeEmailRequest BuildEmailRequest(
            NoticeSettings s,
            Domain.Rolls.RollRegistry roll,
            NoticeRunLog log)
        {
            var req = new NoticeEmailRequest
            {
                Notice = s.Notice,
                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",
                RecipientName = log.RecipientName ?? "",
                RecipientEmail = log.RecipientEmail ?? "",
                FinancialYearsText = s.FinancialYearsText,
                IsSection52Review = s.IsSection52Review,
                InvalidKind = s.IsInvalidOmission == true
                                     ? InvalidNoticeKind.InvalidOmission
                                     : InvalidNoticeKind.InvalidObjection,
                Items = new List<NoticeEmailPropertyItem>
                {
                    new()
                    {
                        PropertyDesc = log.PropertyDesc ?? "",
                        ObjectionNo  = log.ObjectionNo,
                        AppealNo     = log.AppealNo
                    }
                }
            };

            // S49 — inspection dates
            if (s.Notice == NoticeKind.S49)
            {
                req.InspectionStart = s.ObjectionStartDate.HasValue
                    ? DateOnly.FromDateTime(s.ObjectionStartDate.Value) : null;
                req.InspectionEnd = s.ObjectionEndDate.HasValue
                    ? DateOnly.FromDateTime(s.ObjectionEndDate.Value) : null;
                req.ExtendedEnd = s.ExtensionDate.HasValue
                    ? DateOnly.FromDateTime(s.ExtensionDate.Value) : null;
            }

            return req;
        }

        // ── Save .eml file (RFC 2822 MIME with HTML body + PDF attachment) ───
        private static async Task<string> SaveEmlAsync(
    string emlPath,
    string toEmail,
    string subject,
    string bodyHtml,
    string pdfPath,
    CancellationToken ct,
    string? ccAddress = null,
    string? fromAddress = null,
    string? fromName = null)
        {
            var folderPath = Path.GetDirectoryName(emlPath);

            if (string.IsNullOrWhiteSpace(folderPath))
                throw new InvalidOperationException($"Invalid EML path: {emlPath}");

            Directory.CreateDirectory(folderPath);

            if (string.IsNullOrWhiteSpace(toEmail))
                throw new InvalidOperationException("Recipient email is empty. Cannot save EML copy.");

            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                throw new FileNotFoundException($"PDF not found: {pdfPath}");

            var pdfBytes = await File.ReadAllBytesAsync(pdfPath, ct);
            var pdfB64 = Convert.ToBase64String(pdfBytes);
            var pdfName = Path.GetFileName(pdfPath);

            var boundary = $"----=_Part_{Guid.NewGuid():N}";
            var bodyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bodyHtml ?? ""));
            var now = DateTime.Now.ToString("ddd, dd MMM yyyy HH:mm:ss zzz");

            var safeSubject = string.IsNullOrWhiteSpace(subject)
                ? "Notice"
                : subject.Replace("\r", " ").Replace("\n", " ").Trim();

            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(fromName) && !string.IsNullOrWhiteSpace(fromAddress))
                sb.AppendLine($"From: {EncodeMailHeader(fromName)} <{fromAddress}>");
            else if (!string.IsNullOrWhiteSpace(fromAddress))
                sb.AppendLine($"From: {fromAddress}");

            sb.AppendLine($"Date: {now}");
            sb.AppendLine($"To: {toEmail}");

            if (!string.IsNullOrWhiteSpace(ccAddress))
                sb.AppendLine($"Cc: {ccAddress}");

            sb.AppendLine($"Subject: {EncodeMailHeader(safeSubject)}");
            sb.AppendLine("MIME-Version: 1.0");
            sb.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
            sb.AppendLine();
            sb.AppendLine($"--{boundary}");
            sb.AppendLine("Content-Type: text/html; charset=utf-8");
            sb.AppendLine("Content-Transfer-Encoding: base64");
            sb.AppendLine();

            WriteBase64Lines(sb, bodyB64);

            sb.AppendLine();
            sb.AppendLine($"--{boundary}");
            sb.AppendLine($"Content-Type: application/pdf; name=\"{pdfName}\"");
            sb.AppendLine("Content-Transfer-Encoding: base64");
            sb.AppendLine($"Content-Disposition: attachment; filename=\"{pdfName}\"");
            sb.AppendLine();

            WriteBase64Lines(sb, pdfB64);

            sb.AppendLine();
            sb.AppendLine($"--{boundary}--");

            if (File.Exists(emlPath))
            {
                var name = Path.GetFileNameWithoutExtension(emlPath);
                var ext = Path.GetExtension(emlPath);
                emlPath = Path.Combine(folderPath, $"{name}_{Guid.NewGuid():N}{ext}");
            }

            await File.WriteAllTextAsync(emlPath, sb.ToString(), Encoding.UTF8, ct);

            return emlPath;
        }

        private static void WriteBase64Lines(StringBuilder sb, string base64)
        {
            for (int i = 0; i < base64.Length; i += 76)
                sb.AppendLine(base64.Substring(i, Math.Min(76, base64.Length - i)));
        }

       
        private static string EncodeMailHeader(string value)
        {
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        // ── Send one email via SMTP ──────────────────────────────────────────
        private async Task SendOneEmailAsync(
            string subject,
            string bodyHtml,
            NoticeRunLog log,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(log.PdfPath) || !File.Exists(log.PdfPath))
                throw new FileNotFoundException($"PDF not found: {log.PdfPath}");

            if (string.IsNullOrWhiteSpace(_emailOpt.Smtp?.Host))
                throw new InvalidOperationException("Email:Smtp:Host is not configured.");

            using var msg = new MailMessage
            {
                From = new MailAddress(_emailOpt.FromAddress, _emailOpt.FromName),
                Subject = subject,
                Body = bodyHtml,
                IsBodyHtml = true
            };

            msg.To.Add(new MailAddress(log.RecipientEmail!.Trim()));

            // CC from appsettings → Email:CcAddress (all notice types S49–IN)
            if (!string.IsNullOrWhiteSpace(_emailOpt.CcAddress))
                msg.CC.Add(new MailAddress(_emailOpt.CcAddress.Trim(),
                    string.IsNullOrWhiteSpace(_emailOpt.CcName) ? "" : _emailOpt.CcName.Trim()));

            var pdfBytes = await File.ReadAllBytesAsync(log.PdfPath, ct);
            var attachment = new Attachment(new MemoryStream(pdfBytes), Path.GetFileName(log.PdfPath), "application/pdf");
            msg.Attachments.Add(attachment);

            using var smtp = new SmtpClient(_emailOpt.Smtp.Host, _emailOpt.Smtp.Port)
            {
                EnableSsl = _emailOpt.Smtp.EnableSsl,
                Credentials = new System.Net.NetworkCredential(
                    _emailOpt.Smtp.Username ?? "",
                    _emailOpt.Smtp.Password ?? "")
            };

            await Task.Run(() => smtp.Send(msg), ct);
        }
        // ── S53: fetch email data from Objection_MVD ────────────────────────
        private async Task<S53MvdEmailData?> FetchS53MvdEmailDataAsync(
         NoticeKind notice,
         int rollId,
         string objectionNo,
         string batchName,
         string objectorType,
         CancellationToken ct)
        {
            try
            {
                var cs = _config.GetConnectionString("DefaultConnection")
                         ?? throw new InvalidOperationException("DefaultConnection not configured.");

                var proc = notice == NoticeKind.S53Rev
                    ? "dbo.S53Rev_GetMvdRow"
                    : "dbo.S53_GetMvdRow";

                await using var conn = new SqlConnection(cs);
                await using var cmd = new SqlCommand(proc, conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                cmd.Parameters.AddWithValue("@RollId", rollId);
                cmd.Parameters.AddWithValue("@ObjectionNo", objectionNo);
                cmd.Parameters.AddWithValue("@BatchName", batchName);
                cmd.Parameters.AddWithValue("@ObjectorType", objectorType);

                await conn.OpenAsync(ct);
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                if (!await reader.ReadAsync(ct))
                    return null;

                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                    colMap[reader.GetName(i)] = i;

                string? Str(string col) =>
                    colMap.TryGetValue(col, out var o) && !reader.IsDBNull(o)
                        ? reader.GetValue(o)?.ToString()
                        : null;

                return new S53MvdEmailData
                {
                    ObjectionNo = Str("Objection_No") ?? objectionNo,
                    PropertyDesc = Str("PropertyDesc"),
                    Email = Str("Email")
                };
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    ex,
                    "FetchS53MvdEmailDataAsync failed for ObjectionNo={No}, Notice={Notice}",
                    objectionNo,
                    notice);

                return null;
            }
        }

        private sealed class S53MvdEmailData
        {
            public string? ObjectionNo { get; set; }
            public string? PropertyDesc { get; set; }
            public string? Email { get; set; }
        }

        // ══════════════════════════════════════════════════════════════════
        // Fresh email fetch — one method per notice type.
        // Always reads from the source table, never from the run log cache.
        // Falls back to null (caller then uses log.RecipientEmail safely).
        // ══════════════════════════════════════════════════════════════════

        private async Task<string?> FetchFreshEmailAsync(
            NoticeSettings settings,
            NoticeRunLog log,
            string batchName,
            CancellationToken ct)
        {
            return settings.Notice switch
            {
                NoticeKind.S49 => await FetchS49EmailAsync(settings.RollId, log.PremiseId, ct),

                NoticeKind.S51 => await FetchS51EmailAsync(settings.RollId, log.ObjectionNo, ct),

                NoticeKind.S52 => await FetchS52EmailAsync(
                    settings.RollId,
                    log.AppealNo ?? "",
                    settings.IsSection52Review == true,
                    string.IsNullOrWhiteSpace(log.RecipientName) ? null : log.RecipientName.Trim(),
                    ct),

                NoticeKind.S53 or NoticeKind.S53Rev => (await FetchS53MvdEmailDataAsync(
     settings.Notice,
     settings.RollId,
     log.ObjectionNo ?? "",
     batchName,
     string.IsNullOrWhiteSpace(log.RecipientName) ? "Owner" : log.RecipientName.Trim(),
     ct))?.Email,

                // DJ / IN / S78 are snapshot-based — no per-row email table
                _ => null
            };
        }

        // ── S49: SAP postal table via S49_Step3_LoadPremise SP ────────────────
        private async Task<string?> FetchS49EmailAsync(
            int rollId, string? premiseId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(premiseId)) return null;
            try
            {
                var (_, contact) = await _s49Repo.LoadPremiseAsync(rollId, premiseId, ct);
                return string.IsNullOrWhiteSpace(contact?.Email) ? null : contact.Email.Trim();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "FetchS49EmailAsync failed for PremiseId={Id} — using cached email", premiseId);
                return null;
            }
        }

        // ── S51: Section51Table via S51_GetNoticeRow SP ───────────────────────
        private async Task<string?> FetchS51EmailAsync(
            int rollId, string? objectionNo, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(objectionNo)) return null;
            try
            {
                var cs = _config.GetConnectionString("DefaultConnection")
                         ?? throw new InvalidOperationException("DefaultConnection not configured.");

                await using var cn = new SqlConnection(cs);
                await using var cmd = new SqlCommand("dbo.S51_GetNoticeRow", cn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };
                cmd.Parameters.AddWithValue("@RollId", rollId);
                cmd.Parameters.AddWithValue("@ObjectionNo", objectionNo);

                await cn.OpenAsync(ct);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                if (!await rd.ReadAsync(ct)) return null;

                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < rd.FieldCount; i++) colMap[rd.GetName(i)] = i;

                return colMap.TryGetValue("RecipientEmail", out var o) && !rd.IsDBNull(o)
                    ? rd.GetString(o)?.Trim()
                    : null;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "FetchS51EmailAsync failed for ObjectionNo={No} — using cached email", objectionNo);
                return null;
            }
        }

        // ── S52: fetch current email from Appeal_Decision at send time ────────
        // Using the same SP as the print service so the source of truth is always
        // the Appeal_Decision table — not whatever was cached in the run log.
        private async Task<string?> FetchS52EmailAsync(
            int rollId,
            string appealNo,
            bool isReview,
            string? appealType,
            CancellationToken ct)
        {
            try
            {
                var cs = _config.GetConnectionString("DefaultConnection")
                         ?? throw new InvalidOperationException("DefaultConnection not configured.");

                var proc = isReview
                    ? "dbo.S52_Preview_SelectReviewTop1"
                    : "dbo.S52_Preview_SelectAppealTop1";

                await using var cn = new SqlConnection(cs);
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = proc;
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 30;
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
                cmd.Parameters.Add(new SqlParameter("@AppealNo", SqlDbType.VarChar, 50) { Value = appealNo.Trim() });
                if (!string.IsNullOrWhiteSpace(appealType))
                    cmd.Parameters.Add(new SqlParameter("@AppealType", SqlDbType.NVarChar, 50) { Value = appealType!.Trim() });

                await cn.OpenAsync(ct);
                await using var rd = await cmd.ExecuteReaderAsync(ct);

                if (!await rd.ReadAsync(ct)) return null;

                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < rd.FieldCount; i++) colMap[rd.GetName(i)] = i;

                return colMap.TryGetValue("Email", out var o) && !rd.IsDBNull(o)
                    ? rd.GetString(o)
                    : null;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "FetchS52EmailAsync failed for AppealNo={No} — using cached run log email",
                    appealNo);
                return null;   // fall back to log.RecipientEmail safely
            }
        }
        private static string BuildSentNoticeEmlFileName(
      NoticeKind notice,
      string? objectionNo,
      string? appealNo,
      string? propertyDesc,
      string? recipientEmail)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var email = MakeSafeFilePart(recipientEmail);
            if (string.IsNullOrWhiteSpace(email))
                email = "NoEmail";

            if (notice == NoticeKind.S53Rev)
            {
                var objectionKey = MakeSafeFilePart(objectionNo);

                if (string.IsNullOrWhiteSpace(objectionKey))
                    objectionKey = MakeSafeFilePart(propertyDesc);

                if (string.IsNullOrWhiteSpace(objectionKey))
                    objectionKey = "Notice";

                return $"email_{objectionKey}_{email}_RevisedMVD_{stamp}.eml";
            }

            string key;

            if (notice == NoticeKind.S49)
            {
                key = MakeSafeFilePart(propertyDesc);

                if (string.IsNullOrWhiteSpace(key))
                    key = "Property";
            }
            else
            {
                key = MakeSafeFilePart(objectionNo);

                if (string.IsNullOrWhiteSpace(key))
                    key = MakeSafeFilePart(appealNo);

                if (string.IsNullOrWhiteSpace(key))
                    key = MakeSafeFilePart(propertyDesc);

                if (string.IsNullOrWhiteSpace(key))
                    key = "Notice";
            }

            return $"email_{key}_{email}_{stamp}.eml";
        }

        private static string MakeSafeFilePart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var clean = value.Trim();

            foreach (var c in Path.GetInvalidFileNameChars())
                clean = clean.Replace(c, '_');

            clean = clean
                .Replace(" ", "_")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_");

            while (clean.Contains("__"))
                clean = clean.Replace("__", "_");

            return clean.Trim('_');
        }

        private static string BuildEmlPathNextToPdf(
            string pdfPath,
            NoticeKind notice,
            string? objectionNo,
            string? appealNo,
            string? propertyDesc,
            string? recipientEmail)
        {
            var folder = Path.GetDirectoryName(pdfPath);

            if (string.IsNullOrWhiteSpace(folder))
                throw new InvalidOperationException($"Invalid PDF path: {pdfPath}");

            Directory.CreateDirectory(folder);

            var emlFileName = BuildSentNoticeEmlFileName(
                notice,
                objectionNo,
                appealNo,
                propertyDesc,
                recipientEmail);

            return Path.Combine(folder, emlFileName);
        }
    }
}
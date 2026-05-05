using GV23_Notice.Data;
using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.Storage;
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

        private const int HardMaxEmails = 2000;

        public NoticeBatchEmailService(
            AppDbContext db,
            IOptions<EmailOptions> emailOpt,
            INoticeEmailTemplateService templates,
            INoticePathService paths,
            IS49RollRepository s49Repo,
            IConfiguration config,
            ILogger<NoticeBatchEmailService> log)
        {
            _db = db;
            _emailOpt = emailOpt.Value;
            _templates = templates;
            _paths = paths;
            _s49Repo = s49Repo;
            _config = config;
            _log = log;
        }

        // ── Count ready-to-send records ──────────────────────────────────────
        public async Task<int> CountSelectedRecordsAsync(
            IEnumerable<int> batchIds, CancellationToken ct)
        {
            var ids = batchIds.Distinct().ToList();
            return await _db.NoticeRunLogs
                .Where(r => ids.Contains(r.NoticeBatchId)
                         && r.Status == RunStatus.Printed
                         && r.RecipientEmail != null
                         && r.RecipientEmail != "")
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

            var settings = await _db.NoticeSettings.AsNoTracking()
                               .FirstOrDefaultAsync(s => s.ApprovalKey == workflowKey
                                                      || s.WorkflowKey == workflowKey, ct)
                           ?? throw new InvalidOperationException("Workflow settings not found.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == settings.RollId, ct)
                       ?? throw new InvalidOperationException("Roll not found.");

            // Load batches so we can build eml paths per batch
            var batches = await _db.NoticeBatches.AsNoTracking()
                .Where(b => ids.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct);

            var runLogs = await _db.NoticeRunLogs
                .Where(r => ids.Contains(r.NoticeBatchId)
                         && r.Status == RunStatus.Printed
                         && r.RecipientEmail != null
                         && r.RecipientEmail != ""
                         && r.PdfPath != null)
                .OrderBy(r => r.NoticeBatchId).ThenBy(r => r.Id)
                .ToListAsync(ct);

            var result = new SendBatchEmailResult { BatchesProcessed = ids.Count };

            var maxSend = Math.Min(HardMaxEmails, _emailOpt.Limits?.MaxSendPerBatch ?? HardMaxEmails);
            if (runLogs.Count > maxSend)
            {
                result.ErrorMessage = $"Selection contains {runLogs.Count} emails but the limit is {maxSend}. " +
                                      "Please select fewer batches.";
                return result;
            }

            var delay = Math.Max(0, _emailOpt.Limits?.DelayMsBetweenSends ?? 0);

            foreach (var log in runLogs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    batches.TryGetValue(log.NoticeBatchId, out var batch);
                    var batchName = batch?.BatchName ?? "Batch";

                    // Build email body using the same template service as the kickoff preview
                    var req = BuildEmailRequest(settings, roll, log);
                    var (subject, bodyHtml) = _templates.Build(req);

                    string emlPath;
                    if (settings.Notice == NoticeKind.S51)
                    {
                        var emlKey = log.ObjectionNo ?? log.PremiseId ?? log.Id.ToString();
                        var emlDesc = log.PropertyDesc ?? emlKey;
                        emlPath = _paths.BuildS51EmlPath(roll, emlKey, emlDesc);
                    }
                    else if (settings.Notice == NoticeKind.S52)
                    {
                        var isReview = settings.IsSection52Review == true;
                        var emlKey = log.AppealNo ?? log.Id.ToString();
                        var emlDesc = log.PropertyDesc ?? emlKey;
                        // RecipientName stores "Prop_Owner" for companion rows
                        var isPropOwner = string.Equals(log.RecipientName, "Prop_Owner",
                                              StringComparison.OrdinalIgnoreCase)
                                       || (log.PdfPath != null && log.PdfPath.Contains("_Prop_Owner",
                                              StringComparison.OrdinalIgnoreCase));
                        emlPath = _paths.BuildS52EmlPath(roll, emlKey, emlDesc, isReview, isPropOwner);
                    }
                    else if (settings.Notice == NoticeKind.S53)
                    {
                        // S53: .eml sits next to the PDF — just change extension
                        // e.g. GV23-Sup2-521_PORTION_16_ERF_153_RIVER_CLUB_MVD.eml
                        if (!string.IsNullOrWhiteSpace(log.PdfPath))
                        {
                            emlPath = Path.ChangeExtension(log.PdfPath, ".eml");
                        }
                        else
                        {
                            // Fallback if PdfPath not set yet
                            var emlKey = log.ObjectionNo ?? log.Id.ToString();
                            var emlDesc = log.PropertyDesc ?? emlKey;
                            emlPath = _paths.BuildBatchEmlPath(roll, settings.Notice, batchName, emlDesc);
                        }

                        // For S53 re-build the email body from Objection_MVD so it
                        // contains the correct GV + MVD values, not just the run log stub
                        var objectorType = string.IsNullOrWhiteSpace(log.RecipientName) ? "Owner" : log.RecipientName.Trim();
                        var mvdEmailData = await FetchS53MvdEmailDataAsync(
                            batch?.RollId ?? settings.RollId, log.ObjectionNo!, batchName, objectorType, ct);

                        if (mvdEmailData != null)
                        {
                            var s53Req = BuildEmailRequest(settings, roll, log);
                            // Enrich property item with real values from Objection_MVD
                            if (s53Req.Items.Count > 0)
                            {
                                s53Req.Items[0].PropertyDesc = mvdEmailData.PropertyDesc
                                                               ?? log.PropertyDesc ?? "";
                                s53Req.Items[0].ObjectionNo = mvdEmailData.ObjectionNo
                                                               ?? log.ObjectionNo;
                            }
                            var (s53Subject, s53Body) = _templates.Build(s53Req);
                            subject = s53Subject;
                            bodyHtml = s53Body;
                        }
                    }
                    else
                    {
                        emlPath = _paths.BuildBatchEmlPath(
                            roll, settings.Notice, batchName,
                            log.PropertyDesc ?? log.ObjectionNo ?? log.PremiseId ?? log.Id.ToString());
                    }

                    await SaveEmlAsync(emlPath, log.RecipientEmail!, subject, bodyHtml, log.PdfPath!, ct,
                        string.IsNullOrWhiteSpace(_emailOpt.CcAddress) ? null : _emailOpt.CcAddress.Trim());
                    log.EmlPath = emlPath;

                    // Send via SMTP
                    await SendOneEmailAsync(subject, bodyHtml, log, ct);

                    log.Status = RunStatus.Sent;
                    log.SentAtUtc = DateTime.UtcNow;

                    if (settings.Notice == NoticeKind.S49 && !string.IsNullOrWhiteSpace(log.PremiseId))
                        await _s49Repo.MarkEmailSentAsync(roll.RollId, log.PremiseId, ct);

                    result.Sent++;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Email send failed for RunLog {Id}", log.Id);
                    log.Status = RunStatus.Failed;
                    log.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                    result.Failed++;
                }

                await _db.SaveChangesAsync(ct);

                if (delay > 0)
                    await Task.Delay(delay, ct);
            }

            // Mark Printed logs with no email as NoEmail
            var noEmailLogs = await _db.NoticeRunLogs
                .Where(r => ids.Contains(r.NoticeBatchId)
                         && r.Status == RunStatus.Printed
                         && (r.RecipientEmail == null || r.RecipientEmail == ""))
                .ToListAsync(ct);

            foreach (var log in noEmailLogs)
            {
                log.Status = RunStatus.NoEmail;
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
        private static async Task SaveEmlAsync(
            string emlPath,
            string toEmail,
            string subject,
            string bodyHtml,
            string pdfPath,
            CancellationToken ct,
            string? ccAddress = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(emlPath)!);

            var pdfBytes = await File.ReadAllBytesAsync(pdfPath, ct);
            var pdfB64 = Convert.ToBase64String(pdfBytes);
            var pdfName = Path.GetFileName(pdfPath);
            var boundary = $"----=_Part_{Guid.NewGuid():N}";
            var bodyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bodyHtml));
            var now = DateTime.Now.ToString("ddd, dd MMM yyyy HH:mm:ss zzz");

            var sb = new StringBuilder();
            sb.AppendLine($"Date: {now}");
            sb.AppendLine($"To: {toEmail}");
            if (!string.IsNullOrWhiteSpace(ccAddress))
                sb.AppendLine($"Cc: {ccAddress}");
            sb.AppendLine($"Subject: {subject}");
            sb.AppendLine("MIME-Version: 1.0");
            sb.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
            sb.AppendLine();
            sb.AppendLine($"--{boundary}");
            sb.AppendLine("Content-Type: text/html; charset=utf-8");
            sb.AppendLine("Content-Transfer-Encoding: base64");
            sb.AppendLine();

            // base64 HTML in 76-char lines
            for (int i = 0; i < bodyB64.Length; i += 76)
                sb.AppendLine(bodyB64.Substring(i, Math.Min(76, bodyB64.Length - i)));

            sb.AppendLine();
            sb.AppendLine($"--{boundary}");
            sb.AppendLine($"Content-Type: application/pdf; name=\"{pdfName}\"");
            sb.AppendLine("Content-Transfer-Encoding: base64");
            sb.AppendLine($"Content-Disposition: attachment; filename=\"{pdfName}\"");
            sb.AppendLine();

            for (int i = 0; i < pdfB64.Length; i += 76)
                sb.AppendLine(pdfB64.Substring(i, Math.Min(76, pdfB64.Length - i)));

            sb.AppendLine();
            sb.AppendLine($"--{boundary}--");

            await File.WriteAllTextAsync(emlPath, sb.ToString(), Encoding.UTF8, ct);
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

                await using var conn = new SqlConnection(cs);
                await using var cmd = new SqlCommand("dbo.S53_GetMvdRow", conn)
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

                if (!await reader.ReadAsync(ct)) return null;

                // Column map — safe against missing columns
                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++) colMap[reader.GetName(i)] = i;

                string? Str(string col) =>
                    colMap.TryGetValue(col, out var o) && !reader.IsDBNull(o)
                        ? reader.GetValue(o)?.ToString() : null;

                return new S53MvdEmailData
                {
                    ObjectionNo = Str("Objection_No") ?? objectionNo,
                    PropertyDesc = Str("PropertyDesc"),
                    Email = Str("Email"),
                };
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "FetchS53MvdEmailDataAsync failed for ObjectionNo={No} — using run log data",
                    objectionNo);
                return null;
            }
        }

        private sealed class S53MvdEmailData
        {
            public string? ObjectionNo { get; set; }
            public string? PropertyDesc { get; set; }
            public string? Email { get; set; }
        }
    }
}
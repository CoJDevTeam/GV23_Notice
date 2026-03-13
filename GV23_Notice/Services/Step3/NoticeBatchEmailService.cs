using GV23_Notice.Data;
using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
        private readonly ILogger<NoticeBatchEmailService> _log;
        private readonly IConfiguration _config;

        private const int HardMaxEmails = 2000;

        public NoticeBatchEmailService(
            AppDbContext db,
            IOptions<EmailOptions> emailOpt,
            INoticeEmailTemplateService templates,
            INoticePathService paths,
            IS49RollRepository s49Repo,
            ILogger<NoticeBatchEmailService> log,
            IConfiguration config)
        {
            _db = db;
            _emailOpt = emailOpt.Value;
            _templates = templates;
            _paths = paths;
            _s49Repo = s49Repo;
            _log = log;
            _config = config;
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

                   

                    // ── S53: paired email logic ───────────────────────────────────────
                    // Mirrors the print pairing:
                    //   Owner             → 1 email  (sent here)
                    //   Representative    → 2 emails (Representative + Owner_Rep, both sent here)
                    //   Owner_Rep         → secondary, already sent by Representative RunLog — mark Sent
                    //   Third_Party       → 2 emails (Third_Party + Owner_Third_Party, both sent here)
                    //   Owner_Third_Party → secondary, already sent by Third_Party RunLog — mark Sent
                    if (settings.Notice == NoticeKind.S53)
                    {
                        var objectorType = log.RecipientName ?? "Owner";

                        // Secondary RunLogs: primary already sent their email
                        if (objectorType == "Owner_Rep" || objectorType == "Owner_Third_Party")
                        {
                            log.Status = RunStatus.Sent;
                            log.SentAtUtc = DateTime.UtcNow;
                            result.Sent++;
                            await _db.SaveChangesAsync(ct);
                            continue;
                        }

                        var cs53 = _config.GetConnectionString("DefaultConnection")
                                   ?? throw new InvalidOperationException("DefaultConnection not configured.");

                        // ── Primary email (this RunLog's own row) ────────────────────
                        var primaryPdfPath = log.PdfPath
                            ?? throw new FileNotFoundException(
                                $"S53 RunLog {log.Id} has no PdfPath — was it printed?");
                        var primaryEmlPath = log.EmlPath
                            ?? _paths.BuildS53EmlPath(roll, log.ObjectionNo!, log.PropertyDesc ?? "", objectorType);

                        await SaveEmlAsync(primaryEmlPath, log.RecipientEmail!, subject, bodyHtml, primaryPdfPath, ct);
                        await SendOneEmailAsync(subject, bodyHtml, log, ct);
                        log.EmlPath = primaryEmlPath;

                        // ── Paired email (Owner_Rep / Owner_Third_Party row) ──────────
                        var pairedType = objectorType == "Representative" ? "Owner_Rep" : "Owner_Third_Party";
                        var pairedRow = await FetchS53MvdEmailRowAsync(cs53, batch!, log.ObjectionNo!, batch!.BatchName, pairedType, ct);

                        if (pairedRow.email != null)
                        {
                            var pairedPdfPath = _paths.BuildS53PdfPath(roll, log.ObjectionNo!, log.PropertyDesc ?? "", pairedType);
                            var pairedEmlPath = _paths.BuildS53EmlPath(roll, log.ObjectionNo!, log.PropertyDesc ?? "", pairedType);

                            if (File.Exists(pairedPdfPath))
                            {
                                var pairedReq = BuildEmailRequest(settings, roll, log);
                                pairedReq.RecipientEmail = pairedRow.email;
                                var (pairedSubject, pairedBody) = _templates.Build(pairedReq);
                                await SaveEmlAsync(pairedEmlPath, pairedRow.email, pairedSubject, pairedBody, pairedPdfPath, ct);

                                var pairedSmtpLog = new NoticeRunLog
                                {
                                    NoticeBatchId = log.NoticeBatchId,
                                    ObjectionNo = log.ObjectionNo,
                                    PremiseId = log.PremiseId,
                                    RecipientEmail = pairedRow.email,
                                    RecipientName = pairedType,
                                    PropertyDesc = log.PropertyDesc,
                                    PdfPath = pairedPdfPath,
                                    EmlPath = pairedEmlPath
                                };
                                await SendOneEmailAsync(pairedSubject, pairedBody, pairedSmtpLog, ct);
                                _log.LogInformation("S53 paired email sent: {ObjectionNo} {PairedType}", log.ObjectionNo, pairedType);
                            }
                            else
                            {
                                _log.LogWarning("S53 paired PDF not found, skipping paired email: {Path}", pairedPdfPath);
                            }
                        }
                        else
                        {
                            _log.LogWarning("S53 paired row has no email, skipping: {ObjectionNo} {PairedType}", log.ObjectionNo, pairedType);
                        }

                        log.Status = RunStatus.Sent;
                        log.SentAtUtc = DateTime.UtcNow;
                        result.Sent++;
                        await _db.SaveChangesAsync(ct);
                        continue;
                    }
                    // ── END S53 ──────────────────────────────────────────────────────

                    if (settings.Notice == NoticeKind.DJ)
                    {
                        var pdfPath = log.PdfPath
                            ?? throw new FileNotFoundException(
                                $"DJ RunLog {log.Id} has no PdfPath — was it printed?");

                        var djEmlPath = log.EmlPath
                            ?? _paths.BuildDjEmlPath(roll,
                                log.ObjectionNo ?? log.Id.ToString(),
                                log.PropertyDesc ?? "");

                        await SaveEmlAsync(djEmlPath, log.RecipientEmail!, subject, bodyHtml, pdfPath, ct);
                        await SendOneEmailAsync(subject, bodyHtml, log, ct);

                        log.EmlPath = djEmlPath;
                        log.Status = RunStatus.Sent;
                        log.SentAtUtc = DateTime.UtcNow;
                        result.Sent++;
                        await _db.SaveChangesAsync(ct);
                        if (delay > 0) await Task.Delay(delay, ct);
                        continue;
                    }

                    // ── IN: email with attached PDF ───────────────────────────────────
                    if (settings.Notice == NoticeKind.IN)
                    {
                        var isOmission = (log.RecipientName ?? "")
                            .Equals("InvalidOmission", StringComparison.OrdinalIgnoreCase);

                        var pdfPath = log.PdfPath
                            ?? throw new FileNotFoundException(
                                $"IN RunLog {log.Id} has no PdfPath — was it printed?");

                        var inEmlPath = log.EmlPath
                            ?? _paths.BuildInEmlPath(roll,
                                log.ObjectionNo ?? log.Id.ToString(),
                                log.PropertyDesc ?? "",
                                isOmission);

                        await SaveEmlAsync(inEmlPath, log.RecipientEmail!, subject, bodyHtml, pdfPath, ct);
                        await SendOneEmailAsync(subject, bodyHtml, log, ct);

                        log.EmlPath = inEmlPath;
                        log.Status = RunStatus.Sent;
                        log.SentAtUtc = DateTime.UtcNow;
                        result.Sent++;
                        await _db.SaveChangesAsync(ct);
                        if (delay > 0) await Task.Delay(delay, ct);
                        continue;
                    }

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
                        emlPath = _paths.BuildS52EmlPath(roll, emlKey, emlDesc, isReview);
                    }
                    else
                    {
                        emlPath = _paths.BuildBatchEmlPath(
                            roll, settings.Notice, batchName,
                            log.PropertyDesc ?? log.ObjectionNo ?? log.PremiseId ?? log.Id.ToString());
                    }


                    await SaveEmlAsync(emlPath, log.RecipientEmail!, subject, bodyHtml, log.PdfPath!, ct);
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
        // ── S53: fetch email + address from Objection_MVD for paired row ───
        private static async Task<(string? email, string? addr1)> FetchS53MvdEmailRowAsync(
            string cs,
            NoticeBatch batch,
            string objectionNo,
            string batchName,
            string objectorType,
            CancellationToken ct)
        {
            await using var conn = new SqlConnection(cs);
            await using var cmd = new SqlCommand("dbo.S53_GetMvdRow", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            cmd.Parameters.AddWithValue("@RollId", batch.RollId);
            cmd.Parameters.AddWithValue("@ObjectionNo", objectionNo);
            cmd.Parameters.AddWithValue("@BatchName", batchName);
            cmd.Parameters.AddWithValue("@ObjectorType", objectorType);

            await conn.OpenAsync(ct);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct)) return (null, null);

            string? Str(string col)
            {
                var v = reader[col];
                return v == DBNull.Value ? null : v?.ToString();
            }

            return (Str("Email"), Str("ADDR1"));
        }

        // ── S53: flip Email-Pending → Notice-Sent ────────────────────────────
        private static async Task SetS53NoticeSentAsync(
            string cs, int rollId, string batchName, CancellationToken ct)
        {
            await using var conn = new SqlConnection(cs);
            await using var cmd = new SqlCommand("dbo.S53_Step3_SetNoticeSent", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            cmd.Parameters.AddWithValue("@RollId", rollId);
            cmd.Parameters.AddWithValue("@BatchName", batchName);
            await conn.OpenAsync(ct);
            await cmd.ExecuteNonQueryAsync(ct);
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
            CancellationToken ct)
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
    }
}
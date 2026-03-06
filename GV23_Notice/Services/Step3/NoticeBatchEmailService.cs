using GV23_Notice.Data;
using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        private const int HardMaxEmails = 2000;

        public NoticeBatchEmailService(
            AppDbContext db,
            IOptions<EmailOptions> emailOpt,
            INoticeEmailTemplateService templates,
            INoticePathService paths,
            IS49RollRepository s49Repo,
            ILogger<NoticeBatchEmailService> log)
        {
            _db = db;
            _emailOpt = emailOpt.Value;
            _templates = templates;
            _paths = paths;
            _s49Repo = s49Repo;
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

                    // Save .eml copy to disk
                    var emlPath = _paths.BuildBatchEmlPath(
                        roll, settings.Notice, batchName,
                        log.PropertyDesc ?? log.ObjectionNo ?? log.PremiseId ?? log.Id.ToString());

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
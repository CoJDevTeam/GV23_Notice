using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Email;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace GV23_Notice.Services.Corrections
{
    public sealed class NoticeCorrectionEmailService : INoticeCorrectionEmailService
    {
        private const string RequiredCc = "ValuationEnquiries@joburg.org.za";

        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public NoticeCorrectionEmailService(
            AppDbContext db,
            IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<CorrectionEmailComposeVm> BuildComposeVmAsync(
            int batchId,
            CancellationToken ct)
        {
            var batch = await _db.NoticeCorrectionBatches
                .AsNoTracking()
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == batchId, ct);

            if (batch == null)
                throw new InvalidOperationException("Correction batch was not found.");

            if (batch.Items == null || !batch.Items.Any())
                throw new InvalidOperationException("This correction batch has no correction items.");

            if (batch.Items.Any(x => x.Status != "QA-Approved"))
                throw new InvalidOperationException("Correction email cannot be prepared before QA is approved.");

            var first = batch.Items.First();

            var printKind =
                first.PrintNoticeKind
                ?? batch.PrintNoticeKind
                ?? first.NoticeKind
                ?? batch.NoticeKind
                ?? "";

            var template = await _db.NoticeCorrectionEmailTemplates
                .AsNoTracking()
                .Where(x =>
                    x.NoticeKind == printKind &&
                    x.IsDefault &&
                    x.IsActive)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);

            return new CorrectionEmailComposeVm
            {
                BatchId = batch.Id,

                Subject = string.IsNullOrWhiteSpace(template?.SubjectTemplate)
                    ? BuildCorrectionEmailSubject(batch, first)
                    : ApplyEmailTokens(template.SubjectTemplate, batch, first),

                Body = string.IsNullOrWhiteSpace(template?.BodyTemplate)
                    ? ""
                    : ApplyEmailTokens(template.BodyTemplate, batch, first),

                Cc = EnsureValuationEnquiriesCc(template?.CcTemplate),

                Recipients = batch.Items
                    .OrderBy(x => x.RecipientRole)
                    .ThenBy(x => x.Id)
                    .Select(x => new CorrectionEmailRecipientVm
                    {
                        ItemId = x.Id,
                        ReferenceNo = x.ObjectionNo ?? x.ReferenceNo ?? "",
                        PropertyDesc = x.PropertyDesc ?? "",
                        RecipientRole = x.RecipientRole ?? "",
                        RecipientEmail = x.RecipientEmail ?? "",
                        PdfPath = x.PdfPath ?? ""
                    })
                    .ToList()
            };
        }

        public async Task SendBatchEmailAsync(
            CorrectionEmailComposeVm vm,
            string sentBy,
            CancellationToken ct)
        {
            if (vm == null)
                throw new ArgumentNullException(nameof(vm));

            if (string.IsNullOrWhiteSpace(vm.Subject))
                throw new InvalidOperationException("Email subject is required.");

            if (string.IsNullOrWhiteSpace(vm.Body))
                throw new InvalidOperationException("Email body is required.");

            var batch = await _db.NoticeCorrectionBatches
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == vm.BatchId, ct);

            if (batch == null)
                throw new InvalidOperationException("Correction batch was not found.");

            if (batch.Items == null || !batch.Items.Any())
                throw new InvalidOperationException("This correction batch has no correction items.");

            if (batch.Items.Any(x => x.Status != "QA-Approved"))
                throw new InvalidOperationException("Correction email cannot be sent before QA is approved.");

            foreach (var item in batch.Items)
            {
                ValidateItemReadyForEmail(item);
            }

            var cc = EnsureValuationEnquiriesCc(vm.Cc);
            var userBody = vm.Body.Trim();

            var firstItem = batch.Items
                .OrderBy(x => x.RecipientRole)
                .ThenBy(x => x.Id)
                .First();

            await SaveCorrectionEmailTemplateAsync(
                batch,
                firstItem,
                vm.Subject.Trim(),
                userBody,
                cc,
                sentBy,
                ct);

            // Save the template BEFORE sending. If SMTP fails, the wording is still stored.
            await _db.SaveChangesAsync(ct);

            foreach (var item in batch.Items.OrderBy(x => x.RecipientRole).ThenBy(x => x.Id))
            {
                var subject = BuildCorrectionEmailSubject(batch, item);

                item.EmailSubject = subject;
                item.EmailBody = userBody;
                item.EmailCc = cc;

                try
                {
                    var emlPath = await SendOneCorrectionEmailAsync(
                        batch,
                        item,
                        subject,
                        userBody,
                        cc,
                        sentBy,
                        ct);

                    item.EmlPath = emlPath;
                    item.SentAt = DateTime.Now;
                    item.Status = "Sent";
                    item.ErrorMessage = null;
                }
                catch (Exception ex)
                {
                    item.Status = "Email-Failed";
                    item.ErrorMessage = ex.Message;
                }

                // Save per item so one failure does not lose previous sent results.
                await _db.SaveChangesAsync(ct);
            }

            var hasFailed = batch.Items.Any(x => x.Status == "Email-Failed");
            var hasSent = batch.Items.Any(x => x.Status == "Sent");

            batch.Status = hasFailed && hasSent
                ? "Sent-With-Errors"
                : hasFailed
                    ? "Email-Failed"
                    : "Sent";

            batch.SentAt = DateTime.Now;
            batch.SentBy = sentBy;

            await _db.SaveChangesAsync(ct);
        }

        private async Task<string> SendOneCorrectionEmailAsync(
        NoticeCorrectionBatch batch,
        NoticeCorrectionItem item,
        string subject,
        string body,
        string cc,
        string sentBy,
        CancellationToken ct)
        {
            var smtpHost = _config["Email:Smtp:Host"];
            var smtpPort = _config.GetValue<int?>("Email:Smtp:Port") ?? 25;
            var enableSsl = _config.GetValue<bool?>("Email:Smtp:EnableSsl") ?? false;

            var smtpUser = _config["Email:Smtp:Username"];
            var smtpPassword = _config["Email:Smtp:Password"];

            var fromEmail = _config["Email:FromAddress"];
            var fromName = _config["Email:FromName"] ?? "City of Johannesburg Valuation Services";

            if (string.IsNullOrWhiteSpace(smtpHost))
                throw new InvalidOperationException("SMTP host is not configured. Check Email:Smtp:Host in appsettings.json.");

            if (string.IsNullOrWhiteSpace(fromEmail))
                throw new InvalidOperationException("SMTP from email is not configured. Check Email:FromAddress in appsettings.json.");

            using var message = new MailMessage();

            message.From = new MailAddress(fromEmail, fromName);

            // Must come from NoticeCorrectionItems.RecipientEmail
            message.To.Add(item.RecipientEmail!);

            foreach (var ccEmail in SplitEmails(cc))
            {
                message.CC.Add(ccEmail);
            }

            message.Subject = subject;
            message.Body = body;
            message.IsBodyHtml = false;

            message.Attachments.Add(new Attachment(item.PdfPath!));

            OutboundEmailTracking.Apply(
                message,
                _config,
                item.ReferenceNo);

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(smtpUser))
            {
                client.Credentials = new NetworkCredential(smtpUser, smtpPassword);
            }
            else
            {
                client.UseDefaultCredentials = false;
            }

            await client.SendMailAsync(message, ct);

            return SaveEmailEvidence(batch, item, subject, body, cc, sentBy);
        }

        private async Task SaveCorrectionEmailTemplateAsync(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item,
            string subject,
            string body,
            string cc,
            string createdBy,
            CancellationToken ct)
        {
            var noticeKind =
                item.PrintNoticeKind
                ?? batch.PrintNoticeKind
                ?? item.NoticeKind
                ?? batch.NoticeKind
                ?? "";

            if (string.IsNullOrWhiteSpace(noticeKind))
                throw new InvalidOperationException("Cannot save correction email template because NoticeKind is missing.");

            var templateName = BuildCorrectionTemplateName(batch, item);

            /*
             * One active default correction template per print notice kind.
             * The latest sent wording becomes the default next time.
             */
            var oldDefaults = await _db.NoticeCorrectionEmailTemplates
                .Where(x =>
                    x.NoticeKind == noticeKind &&
                    x.IsDefault &&
                    x.IsActive)
                .ToListAsync(ct);

            foreach (var old in oldDefaults)
            {
                old.IsDefault = false;
                old.IsActive = false;
            }

            var template = new NoticeCorrectionEmailTemplate
            {
                NoticeKind = noticeKind,
                NoticeSubKind = batch.NoticeSubKind,
                TemplateName = templateName,

                SubjectTemplate = subject,
                BodyTemplate = body,
                CcTemplate = cc,

                IsDefault = true,
                IsActive = true,

                CreatedBy = createdBy,
                CreatedAt = DateTime.Now
            };

            _db.NoticeCorrectionEmailTemplates.Add(template);
        }

        private static void ValidateItemReadyForEmail(NoticeCorrectionItem item)
        {
            if (string.IsNullOrWhiteSpace(item.RecipientEmail))
                throw new InvalidOperationException($"Recipient email is missing for {item.ReferenceNo ?? item.ObjectionNo}.");

            if (string.IsNullOrWhiteSpace(item.PdfPath))
                throw new InvalidOperationException($"PDF path is missing for {item.ReferenceNo ?? item.ObjectionNo}.");

            if (!File.Exists(item.PdfPath))
                throw new InvalidOperationException($"PDF file does not exist: {item.PdfPath}");
        }

        private static string BuildCorrectionEmailSubject(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item)
        {
            var printKind =
                item.PrintNoticeKind
                ?? batch.PrintNoticeKind
                ?? item.NoticeKind
                ?? "";

            var printTitle =
                item.PrintNoticeTitle
                ?? batch.PrintNoticeTitle
                ?? NoticeDisplayName(printKind);

            var roll = !string.IsNullOrWhiteSpace(batch.RollShortCode)
                ? batch.RollShortCode
                : "Valuation Roll";

            var property = !string.IsNullOrWhiteSpace(item.PropertyDesc)
                ? item.PropertyDesc
                : "Property";

            var reference = item.ObjectionNo
                ?? item.AppealNo
                ?? item.QueryNo
                ?? item.ReviewNo
                ?? item.ReferenceNo
                ?? batch.ReferenceNo
                ?? "";

            return printKind switch
            {
                "S49" =>
                    $"Correction - Section 49 Valuation Notice - {roll} - {property} - {reference}",

                "S51" =>
                    $"Correction - Section 51 Objection Decision Notice - {roll} - {property} - {reference}",

                "S52" =>
                    $"Correction - Section 52 Appeal Notice - {roll} - {property} - {reference}",

                "S53" =>
                    $"Correction - Section 53 Municipal Valuer's Decision Notice - {roll} - {property} - {reference}",

                "S53Rev" =>
                    $"Correction - Revised Section 53 Municipal Valuer's Decision Notice - {roll} - {property} - {reference}",

                "DJ" =>
                    $"Correction - Dear Johnny Notice - {roll} - {property} - {reference}",

                "IN" =>
                    $"Correction - Invalid Notice - {roll} - {property} - {reference}",

                "S78" =>
                    $"Correction - Section 78 Review Notice - {roll} - {property} - {reference}",

                _ =>
                    $"Correction - {printTitle} - {roll} - {property} - {reference}"
            };
        }

        private static string BuildCorrectionTemplateName(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item)
        {
            var printKind =
                item.PrintNoticeKind
                ?? batch.PrintNoticeKind
                ?? item.NoticeKind
                ?? batch.NoticeKind
                ?? "Notice";

            var printTitle =
                item.PrintNoticeTitle
                ?? batch.PrintNoticeTitle
                ?? NoticeDisplayName(printKind);

            return $"{printTitle} Correction Email Template";
        }

        private static string ApplyEmailTokens(
            string value,
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var printKind =
                item.PrintNoticeKind
                ?? batch.PrintNoticeKind
                ?? item.NoticeKind
                ?? batch.NoticeKind
                ?? "";

            var printTitle =
                item.PrintNoticeTitle
                ?? batch.PrintNoticeTitle
                ?? NoticeDisplayName(printKind);

            var reference =
                item.ObjectionNo
                ?? item.AppealNo
                ?? item.QueryNo
                ?? item.ReviewNo
                ?? item.ReferenceNo
                ?? batch.ReferenceNo
                ?? "";

            return value
                .Replace("{Roll}", batch.RollShortCode ?? "")
                .Replace("{RollName}", batch.RollShortCode ?? "")
                .Replace("{NoticeKind}", printKind)
                .Replace("{NoticeTitle}", printTitle)
                .Replace("{ReferenceNo}", reference)
                .Replace("{ObjectionNo}", item.ObjectionNo ?? "")
                .Replace("{AppealNo}", item.AppealNo ?? "")
                .Replace("{QueryNo}", item.QueryNo ?? "")
                .Replace("{ReviewNo}", item.ReviewNo ?? "")
                .Replace("{PropertyDesc}", item.PropertyDesc ?? "")
                .Replace("{ObjectorType}", item.RecipientRole ?? "")
                .Replace("{RecipientEmail}", item.RecipientEmail ?? "")
                .Replace("{ValuationKey}", item.ValuationKey ?? "")
                .Replace("{PremiseId}", item.PremiseId ?? "");
        }

        private static string NoticeDisplayName(string? noticeKind)
        {
            return noticeKind switch
            {
                "S49" => "Section 49 Valuation Notice",
                "S51" => "Section 51 Objection Decision Notice",
                "S52" => "Section 52 Appeal Notice",
                "S53" => "Section 53 Municipal Valuer's Decision Notice",
                "S53Rev" => "Revised Section 53 Municipal Valuer's Decision Notice",
                "DJ" => "Dear Johnny Notice",
                "IN" => "Invalid Notice",
                "S78" => "Section 78 Review Notice",
                _ => noticeKind ?? "Correction Notice"
            };
        }

        private string EnsureValuationEnquiriesCc(string? cc)
        {
            var requiredCc = _config["Email:CcAddress"];

            if (string.IsNullOrWhiteSpace(requiredCc))
                requiredCc = RequiredCc;

            if (string.IsNullOrWhiteSpace(cc))
                return requiredCc;

            var emails = SplitEmails(cc).ToList();

            if (!emails.Any(x => string.Equals(x, requiredCc, StringComparison.OrdinalIgnoreCase)))
                emails.Add(requiredCc);

            return string.Join("; ", emails);
        }

        private static IEnumerable<string> SplitEmails(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Enumerable.Empty<string>();

            return value
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private string SaveEmailEvidence(
       NoticeCorrectionBatch batch,
       NoticeCorrectionItem item,
       string subject,
       string body,
       string cc,
       string sentBy)
        {
            var printFolder = Path.GetDirectoryName(item.PdfPath);

            if (string.IsNullOrWhiteSpace(printFolder))
                throw new InvalidOperationException("Cannot resolve email evidence folder because PDF path is invalid.");

            Directory.CreateDirectory(printFolder);

            var reference = item.ObjectionNo ?? item.ReferenceNo ?? "Unknown";
            var propertyDesc = item.PropertyDesc ?? "UnknownProperty";

            var printTitle =
                item.PrintNoticeTitle
                ?? batch.PrintNoticeTitle
                ?? item.PrintNoticeKind
                ?? batch.PrintNoticeKind
                ?? item.NoticeKind
                ?? "Notice";

            var objectorType = item.RecipientRole ?? "UnknownObjector";

            var sentAt = DateTimeOffset.Now;

            var fileName =
                $"email_" +
                $"{SafeFile(reference)}_" +
                $"{SafeFile(propertyDesc)}_" +
                $"Corrected_{SafeFile(printTitle)}_" +
                $"{SafeFile(objectorType)}_" +
                $"{sentAt:yyyyMMdd_HHmmss}.eml";

            var emlPath = Path.Combine(printFolder, fileName);

            var fromEmail = _config["Email:FromAddress"] ?? "Propertyinfo@Joburg.org.za";
            var fromName = _config["Email:FromName"] ?? "City of Johannesburg Valuation Services";

            var messageId = $"<{Guid.NewGuid():N}@joburg.org.za>";
            var dateHeader = FormatEmailDate(sentAt);

            var fullBody = new StringBuilder();

            fullBody.AppendLine(body);
            fullBody.AppendLine();
            fullBody.AppendLine();
            fullBody.AppendLine("------------------------------------------------------------");
            fullBody.AppendLine("Email Evidence");
            fullBody.AppendLine("------------------------------------------------------------");
            fullBody.AppendLine($"Sent By: {sentBy}");
            fullBody.AppendLine($"Sent At: {sentAt:yyyy-MM-dd HH:mm:ss}");
            fullBody.AppendLine($"Batch: {batch.CorrectionBatchName}");
            fullBody.AppendLine($"Reference: {reference}");
            fullBody.AppendLine($"Property Description: {propertyDesc}");
            fullBody.AppendLine($"Print Notice: {printTitle}");
            fullBody.AppendLine($"Objector Type: {objectorType}");
            fullBody.AppendLine($"Correction Item Id: {item.Id}");
            fullBody.AppendLine($"Attached PDF: {item.PdfPath}");

            var bodyBytes = Encoding.UTF8.GetBytes(fullBody.ToString());
            var base64Body = Convert.ToBase64String(bodyBytes, Base64FormattingOptions.InsertLineBreaks);

            var sb = new StringBuilder();

            // Proper .eml headers. Outlook uses these to show date/time.
            sb.AppendLine($"From: \"{EscapeHeader(fromName)}\" <{fromEmail}>");
            sb.AppendLine($"To: {item.RecipientEmail}");
            sb.AppendLine($"Cc: {cc}");
            sb.AppendLine($"Subject: {EncodeHeader(subject)}");
            sb.AppendLine($"Date: {dateHeader}");
            sb.AppendLine($"Message-ID: {messageId}");
            sb.AppendLine("MIME-Version: 1.0");
            sb.AppendLine("Content-Type: text/plain; charset=\"utf-8\"");
            sb.AppendLine("Content-Transfer-Encoding: base64");
            sb.AppendLine("X-Correction-Batch: " + EscapeHeader(batch.CorrectionBatchName));
            sb.AppendLine("X-Correction-Reference: " + EscapeHeader(reference));
            sb.AppendLine("X-Correction-Objector-Type: " + EscapeHeader(objectorType));
            sb.AppendLine();

            // Actual body
            sb.AppendLine(base64Body);

            File.WriteAllText(emlPath, sb.ToString(), Encoding.ASCII);

            return emlPath;
        }
        private static string FormatEmailDate(DateTimeOffset value)
        {
            // RFC-style date header that Outlook understands.
            // Example: Tue, 07 Jul 2026 14:39:00 +0200
            var offset = value.Offset;
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            offset = offset.Duration();

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}, {1:00} {2} {3:0000} {4:00}:{5:00}:{6:00} {7}{8:00}{9:00}",
                value.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture),
                value.Day,
                value.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture),
                value.Year,
                value.Hour,
                value.Minute,
                value.Second,
                sign,
                offset.Hours,
                offset.Minutes);
        }

        private static string EncodeHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            // If it is plain ASCII, keep it readable.
            if (value.All(c => c <= 127))
                return value.Replace("\r", "").Replace("\n", "");

            var bytes = Encoding.UTF8.GetBytes(value);
            return "=?utf-8?B?" + Convert.ToBase64String(bytes) + "?=";
        }

        private static string EscapeHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\"", "'");
        }

        private static string SafeFile(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var invalid = Path.GetInvalidFileNameChars();

            var cleaned = new string(
                value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());

            cleaned = cleaned
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_")
                .Trim();

            while (cleaned.Contains("  "))
                cleaned = cleaned.Replace("  ", " ");

            if (cleaned.Length > 120)
                cleaned = cleaned.Substring(0, 120).Trim();

            return cleaned;
        }
    }
}
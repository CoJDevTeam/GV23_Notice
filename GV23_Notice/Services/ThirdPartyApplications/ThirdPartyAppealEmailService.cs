using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public sealed class ThirdPartyAppealEmailService : IThirdPartyAppealEmailService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IThirdPartyAppealDateConfigurationService _dateService;

        public ThirdPartyAppealEmailService(
            AppDbContext db,
            IConfiguration config,
            IThirdPartyAppealDateConfigurationService dateService)
        {
            _db = db;
            _config = config;
            _dateService = dateService;
        }

        public async Task<ThirdPartyAppealSendEmailVm> BuildEmailVmAsync(
     Guid key,
     CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);
            var query = BuildNoticeQuery(settings);

            var rows = await query
                .AsNoTracking()
                .Where(x =>
                    x.Status == "Printed" ||
                    x.Status == "Email-Failed" ||
                    x.Status == "Sent" ||
                    x.Status == "No-Owner-Email")
                .Select(x => new
                {
                    x.Id,
                    x.Appeal_No,
                    x.Objection_No,
                    x.Premise_ID,
                    x.Property_Description,
                    x.Property_Type,
                    x.OwnerName,
                    x.OwnerEmail,
                    x.ThirdPartyName,
                    x.ThirdPartyEmail,
                    x.AdminName,
                    x.AdminEmail,
                    x.PdfPath,
                    x.AppealPackZipPath,
                    x.EmlPath,
                    x.Status,
                    x.ErrorMessage
                })
                .ToListAsync(ct);

            var totalPrinted = rows.Count(x =>
                x.Status == "Printed");

            var totalFailed = rows.Count(x =>
                x.Status == "Email-Failed");

            // Printed notices and failed notices can both be sent.
            var totalReady = totalPrinted + totalFailed;

            var totalSent = rows.Count(x =>
                x.Status == "Sent");

            var totalNoOwnerEmail = rows.Count(x =>
                string.IsNullOrWhiteSpace(x.OwnerEmail) ||
                x.Status == "No-Owner-Email");

            var groups = rows
                .GroupBy(x => NormalizeGroup(x.Property_Type))
                .OrderBy(g => g.Key)
                .Select(g => new ThirdPartyAppealSendEmailGroupVm
                {
                    GroupName = g.Key,

                    Total = g.Count(),

                    ReadyToSend = g.Count(x =>
                        x.Status == "Printed" ||
                        x.Status == "Email-Failed"),

                    Sent = g.Count(x =>
                        x.Status == "Sent"),

                    EmailFailed = g.Count(x =>
                        x.Status == "Email-Failed"),

                    MissingOwnerEmail = g.Count(x =>
                        string.IsNullOrWhiteSpace(x.OwnerEmail) ||
                        x.Status == "No-Owner-Email")
                })
                .ToList();

            // Keep only a small internal sample.
            // The Send view now uses Groups instead of showing every property.
            var items = rows
                .OrderBy(x => x.Appeal_No)
                .Take(20)
                .Select(x => new ThirdPartyAppealSendEmailItemVm
                {
                    Id = x.Id,
                    AppealNo = x.Appeal_No ?? "",
                    ObjectionNo = x.Objection_No ?? "",
                    PremiseId = x.Premise_ID ?? "",
                    PropertyDescription = x.Property_Description ?? "",

                    OwnerName = x.OwnerName ?? "",
                    OwnerEmail = x.OwnerEmail ?? "",

                    ThirdPartyName = x.ThirdPartyName ?? "",
                    ThirdPartyEmail = x.ThirdPartyEmail ?? "",

                    AdminName = x.AdminName ?? "",
                    AdminEmail = x.AdminEmail ?? "",

                    EmailTo = x.OwnerEmail ?? "",

                    EmailCc = string.Join(
                        "; ",
                        new[]
                        {
                    x.ThirdPartyEmail,
                    x.AdminEmail
                        }
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => v!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)),

                    PdfPath = x.PdfPath ?? "",
                    AppealPackZipPath = x.AppealPackZipPath ?? "",
                    EmlPath = x.EmlPath ?? "",

                    Status = x.Status ?? "Pending",
                    ErrorMessage = x.ErrorMessage ?? ""
                })
                .ToList();

            return new ThirdPartyAppealSendEmailVm
            {
                NoticeSettingsId = settings.Id,
                RollId = settings.RollId,

                RollShortCode =
                    await GetRollShortCodeAsync(settings.RollId, ct),

                ValuationPeriod =
                    settings.ValuationPeriodCode ?? "",

                Notice =
                    "Third-Party Appeal Application",

                TotalPrinted = totalPrinted,
                TotalReadyToSend = totalReady,
                TotalSent = totalSent,
                TotalFailed = totalFailed,
                TotalNoOwnerEmail = totalNoOwnerEmail,

                Groups = groups,
                Items = items
            };
        }

        private static string NormalizeGroup(string? propertyType)
        {
            return string.IsNullOrWhiteSpace(propertyType)
                ? "Unspecified"
                : propertyType.Trim();
        }
        public async Task<ThirdPartyAppealEmailResultVm> SendAsync(
            Guid key,
            string sentBy,
            CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            if (settings.Notice != NoticeKind.TPA)
                throw new InvalidOperationException("This email service is only for Third-Party Appeal Application notices.");

            var notices = await BuildNoticeQuery(settings)
                .Where(x => x.Status == "Printed" || x.Status == "Email-Failed")
                .OrderBy(x => x.Appeal_No)
                .ToListAsync(ct);

            var result = new ThirdPartyAppealEmailResultVm
            {
                Total = notices.Count
            };

            foreach (var notice in notices)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(notice.OwnerEmail))
                    {
                        notice.Status = "No-Owner-Email";
                        notice.ErrorMessage = "Owner email is missing.";
                        result.Skipped++;
                        await _db.SaveChangesAsync(ct);
                        continue;
                    }

                    ValidateReadyToSend(notice);

                    var sentAt = DateTime.Now;

                    var responseDate = await _dateService.CalculateResponseDateAsync(
                        startDate: sentAt.Date,
                        responseDays: 51,
                        ct: ct);

                    notice.LetterDate = responseDate.StartDate;
                    notice.ResponseDueDate = responseDate.ResponseDueDate;

                    var subject = BuildSubject(settings, notice);
                    var body = BuildBody(notice);
                    var cc = BuildCc(notice);

                    await SendOneAsync(
                        notice,
                        subject,
                        body,
                        cc,
                        ct);

                    var emlPath = SaveEmailEvidence(
                        settings,
                        notice,
                        subject,
                        body,
                        cc,
                        sentBy,
                        sentAt);

                    notice.EmailSubject = subject;
                    notice.EmailBody = body;
                    notice.EmailTo = notice.OwnerEmail;
                    notice.EmailCc = cc;
                    notice.EmlPath = emlPath;

                    notice.SentAt = sentAt;
                    notice.SentBy = sentBy;
                    notice.Status = "Sent";
                    notice.ErrorMessage = null;

                    result.Sent++;
                }
                catch (Exception ex)
                {
                    notice.Status = "Email-Failed";
                    notice.ErrorMessage = ex.Message;
                    notice.UpdatedAt = DateTime.Now;
                    notice.UpdatedBy = sentBy;

                    result.Failed++;
                }

                await _db.SaveChangesAsync(ct);
            }

            return result;
        }

        public async Task<ThirdPartyAppealEmailProgressVm> GetProgressAsync(
            Guid key,
            CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            var query = BuildNoticeQuery(settings);

            var total = await query.CountAsync(ct);
            var sent = await query.CountAsync(x => x.Status == "Sent", ct);
            var failed = await query.CountAsync(x => x.Status == "Email-Failed", ct);
            var skipped = await query.CountAsync(x => x.Status == "No-Owner-Email", ct);
            var pending = await query.CountAsync(x => x.Status == "Printed", ct);

            return new ThirdPartyAppealEmailProgressVm
            {
                Total = total,
                Sent = sent,
                Failed = failed,
                Skipped = skipped,
                Pending = pending
            };
        }

        private async Task SendOneAsync(
            ThirdPartyAppealApplicationNotice notice,
            string subject,
            string body,
            string cc,
            CancellationToken ct)
        {
            var smtpHost = _config["Email:Smtp:Host"];
            var smtpPort = _config.GetValue<int?>("Email:Smtp:Port") ?? 25;
            var enableSsl = _config.GetValue<bool?>("Email:Smtp:EnableSsl") ?? false;

            var smtpUser = _config["Email:Smtp:Username"];
            var smtpPassword = _config["Email:Smtp:Password"];

            var fromEmail = _config["Email:FromAddress"] ?? "Propertyinfo@Joburg.org.za";
            var fromName = _config["Email:FromName"] ?? "City of Johannesburg Valuation Services";

            if (string.IsNullOrWhiteSpace(smtpHost))
                throw new InvalidOperationException("Email:Smtp:Host is missing in appsettings.");

            using var message = new MailMessage();

            message.From = new MailAddress(fromEmail, fromName);
            message.To.Add(notice.OwnerEmail!);

            foreach (var email in SplitEmails(cc))
                message.CC.Add(email);

            message.Subject = subject;
            message.Body = body;
            message.IsBodyHtml = false;

            message.Attachments.Add(new Attachment(notice.PdfPath!));
            message.Attachments.Add(new Attachment(notice.AppealPackZipPath!));

            using var smtp = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(smtpUser))
            {
                smtp.Credentials = new NetworkCredential(smtpUser, smtpPassword);
            }
            else
            {
                smtp.UseDefaultCredentials = false;
            }

            await smtp.SendMailAsync(message, ct);
        }

        private static void ValidateReadyToSend(
            ThirdPartyAppealApplicationNotice notice)
        {
            if (string.IsNullOrWhiteSpace(notice.PdfPath) || !File.Exists(notice.PdfPath))
                throw new InvalidOperationException($"Formal notice PDF is missing for {notice.Appeal_No}.");

            if (string.IsNullOrWhiteSpace(notice.AppealPackZipPath) || !File.Exists(notice.AppealPackZipPath))
                throw new InvalidOperationException($"Appeal Pack zip is missing for {notice.Appeal_No}.");
        }

        private static string BuildSubject(
            NoticeSettings settings,
            ThirdPartyAppealApplicationNotice notice)
        {
            var period = !string.IsNullOrWhiteSpace(notice.ValuationPeriod?.ToUpper()
                )
                ? notice.ValuationPeriod.ToUpper()
                : settings.ValuationPeriodCode ?? "GENERAL VALUATION ROLL 2023";

            return $"NOTICE OF THIRD PARTY APPEAL APPLICATION FOR THE {period} – {notice.Property_Description?.ToUpper()}";
            

        }

        private static string BuildBody(
            ThirdPartyAppealApplicationNotice notice)
        {
            return
$@"Dear Client,

Property Description: {notice.Property_Description}

Please be advised that the City has received an appeal application relating to the above-mentioned property from a party who is not the registered owner. As this matter may affect your property interests, your attention and participation are required.

Attached herewith is the Valuation Appeal Board formal notice {notice.Appeal_No} for your attention, review, and necessary action.

For any further enquiries or assistance, please contact us via email at {notice.AdminEmail} and valuationenquiries@joburg.org.za.

Your urgent attention is required.

Yours faithfully,
Valuation Appeal Board Secretariat
City of Johannesburg";
        }

        private static string BuildCc(ThirdPartyAppealApplicationNotice notice)
        {
            var cc = new List<string>();

            if (!string.IsNullOrWhiteSpace(notice.ThirdPartyEmail))
                cc.Add(notice.ThirdPartyEmail.Trim());

            if (!string.IsNullOrWhiteSpace(notice.AdminEmail))
                cc.Add(notice.AdminEmail.Trim());

            cc.Add("valuationenquiries@joburg.org.za");

            return string.Join(";", cc.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private string SaveEmailEvidence(
     NoticeSettings settings,
     ThirdPartyAppealApplicationNotice notice,
     string subject,
     string body,
     string cc,
     string sentBy,
     DateTime sentAt)
        {
            if (string.IsNullOrWhiteSpace(notice.Appeal_No))
                throw new InvalidOperationException("Appeal_No is missing.");

            var rootKey = ResolveAppealRootKeyFromAppealNo(notice.Appeal_No);

            var appealRoot = _config[$"Storage:AppealRootsByShortCode:{rootKey}"];

            if (string.IsNullOrWhiteSpace(appealRoot))
            {
                throw new InvalidOperationException(
                    $"Appeal root path is missing in appsettings for key '{rootKey}'.");
            }

            var tpaFolderName =
                _config["Storage:ThirdPartyAppealApplication:PdfFolderName"]
                ?? "THIRD-PARTY APPLICATION";

            var emailCopyFolderName =
                _config["Storage:ThirdPartyAppealApplication:EmailCopyFolderName"]
                ?? "EMAIL COPIES";

            var folder = Path.Combine(
                appealRoot,
                SafeFile(notice.Appeal_No),
                tpaFolderName,
                emailCopyFolderName);

            Directory.CreateDirectory(folder);

            var fileName =
                $"email_{SafeFile(notice.Appeal_No)}_" +
                $"{SafeFile(notice.Property_Description)}_" +
                $"Third-Party Appeal Application_" +
                $"{sentAt:yyyyMMdd_HHmmss}.eml";

            var path = Path.Combine(folder, fileName);

            var fromEmail = _config["Email:FromAddress"] ?? "Propertyinfo@Joburg.org.za";
            var fromName = _config["Email:FromName"] ?? "City of Johannesburg Valuation Services";

            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var base64Body = Convert.ToBase64String(bodyBytes, Base64FormattingOptions.InsertLineBreaks);

            var sb = new StringBuilder();

            sb.AppendLine($"From: \"{EscapeHeader(fromName)}\" <{fromEmail}>");
            sb.AppendLine($"To: {notice.OwnerEmail}");
            sb.AppendLine($"Cc: {cc}");
            sb.AppendLine($"Subject: {EncodeHeader(subject)}");
            sb.AppendLine($"Date: {FormatEmailDate(new DateTimeOffset(sentAt))}");
            sb.AppendLine($"Message-ID: <{Guid.NewGuid():N}@joburg.org.za>");
            sb.AppendLine("MIME-Version: 1.0");
            sb.AppendLine("Content-Type: text/plain; charset=\"utf-8\"");
            sb.AppendLine("Content-Transfer-Encoding: base64");
            sb.AppendLine("X-Notice-Type: Third-Party Appeal Application");
            sb.AppendLine("X-Appeal-No: " + EscapeHeader(notice.Appeal_No));
            sb.AppendLine();
            sb.AppendLine(base64Body);

            File.WriteAllText(path, sb.ToString(), Encoding.ASCII);

            return path;
        }

        private static string ResolveAppealRootKeyFromAppealNo(string? appealNo)
        {
            if (string.IsNullOrWhiteSpace(appealNo))
                return "GV23";

            var value = appealNo.Trim();

            if (value.Contains("APP-GV23-Sup1", StringComparison.OrdinalIgnoreCase))
                return "SUPP 1";

            if (value.Contains("APP-GV23-Sup2", StringComparison.OrdinalIgnoreCase))
                return "SUPP 2";

            if (value.Contains("APP-GV23-Sup3", StringComparison.OrdinalIgnoreCase))
                return "SUPP 3";

            if (value.Contains("APP-GV23-", StringComparison.OrdinalIgnoreCase))
                return "GV23";

            return "GV23";
        }
        private IQueryable<ThirdPartyAppealApplicationNotice> BuildNoticeQuery(
         NoticeSettings settings)
        {
            var q = _db.ThirdPartyAppealApplicationNotices.AsQueryable();

            if (settings.Id > 0)
            {
                var bySettings = q.Where(x => x.NoticeSettingsId == settings.Id);

                if (bySettings.Any())
                    return bySettings;
            }

            /*
             * TPA records are selected by extract/workflow first.
             * If no rows are linked yet, return all imported TPA records.
             * They will be linked during print.
             */
            return q;
        }

        private async Task<NoticeSettings> GetSettingsAsync(
            Guid key,
            CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .FirstOrDefaultAsync(x => x.ApprovalKey == key || x.WorkflowKey == key, ct);

            if (settings == null)
                throw new InvalidOperationException("Workflow settings were not found.");

            return settings;
        }

        private async Task<string> GetRollShortCodeAsync(
            int rollId,
            CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == rollId, ct);

            return roll?.ShortCode ?? "";
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

        private static string SafeFile(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var invalid = Path.GetInvalidFileNameChars();

            var cleaned = new string(
                value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());

            if (cleaned.Length > 120)
                cleaned = cleaned.Substring(0, 120).Trim();

            return cleaned;
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

        private static string EncodeHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (value.All(c => c <= 127))
                return value.Replace("\r", "").Replace("\n", "");

            var bytes = Encoding.UTF8.GetBytes(value);
            return "=?utf-8?B?" + Convert.ToBase64String(bytes) + "?=";
        }

        private static string FormatEmailDate(DateTimeOffset value)
        {
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
    }
}
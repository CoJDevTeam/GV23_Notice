using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    public sealed class ClaThirdPartyApplicationEmailService
        : IClaThirdPartyApplicationEmailService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public ClaThirdPartyApplicationEmailService(
            AppDbContext db,
            IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<ThirdPartyAppealSendEmailVm> BuildEmailVmAsync(
            Guid key,
            CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            if (settings.Notice != NoticeKind.CLA_TPA)
            {
                throw new InvalidOperationException(
                    "This email dashboard is only for CLA Third-Party Application notices.");
            }

            var query = await BuildNoticeQueryAsync(
                settings,
                tracking: false,
                ct);

            var rows = await query
                .Where(x =>
                    x.Status == "Printed" ||
                    x.Status == "Email-Failed" ||
                    x.Status == "Sent" ||
                    x.Status == "No-Owner-Email")
                .Select(x => new
                {
                    x.Id,
                    x.ClaNumber,
                    x.ObjectionNumber,
                    x.PremiseId,
                    x.PropertyDescription,
                    x.RollCategory1,
                    x.OwnerName,
                    x.OwnerEmail,
                    x.ThirdPartyName,
                    x.ThirdPartyEmail,
                    x.AdminEmail,
                    x.PdfPath,
                    x.AppealPackPath,
                  
                    x.Status,
                    x.EmailError
                })
                .ToListAsync(ct);

            var totalPrinted = rows.Count(x =>
                x.Status == "Printed");

            var totalFailed = rows.Count(x =>
                x.Status == "Email-Failed");

            var totalReady = rows.Count(x =>
                x.Status == "Printed" ||
                x.Status == "Email-Failed");

            var totalSent = rows.Count(x =>
                x.Status == "Sent");

            var totalNoOwnerEmail = rows.Count(x =>
                string.IsNullOrWhiteSpace(x.OwnerEmail) ||
                x.Status == "No-Owner-Email");

            var groups = rows
                .GroupBy(x => NormalizeGroup(x.RollCategory1))
                .OrderBy(x => x.Key)
                .Select(group => new ThirdPartyAppealSendEmailGroupVm
                {
                    GroupName = group.Key,
                    Total = group.Count(),

                    ReadyToSend = group.Count(x =>
                        x.Status == "Printed" ||
                        x.Status == "Email-Failed"),

                    Sent = group.Count(x =>
                        x.Status == "Sent"),

                    EmailFailed = group.Count(x =>
                        x.Status == "Email-Failed"),

                    MissingOwnerEmail = group.Count(x =>
                        string.IsNullOrWhiteSpace(x.OwnerEmail) ||
                        x.Status == "No-Owner-Email")
                })
                .ToList();

            var items = rows
                .OrderBy(x => x.ClaNumber)
                .Take(20)
                .Select(x => new ThirdPartyAppealSendEmailItemVm
                {
                    Id = x.Id,

                    // Existing shared TPA VM is reused.
                    AppealNo = x.ClaNumber ?? "",
                    ObjectionNo = x.ObjectionNumber ?? "",
                    PremiseId = x.PremiseId ?? "",
                    PropertyDescription =
                        x.PropertyDescription ?? "",

                    OwnerName = x.OwnerName ?? "",
                    OwnerEmail = x.OwnerEmail ?? "",

                    ThirdPartyName =
                        x.ThirdPartyName ?? "",
                    ThirdPartyEmail =
                        x.ThirdPartyEmail ?? "",

                    AdminName = "",
                    AdminEmail = x.AdminEmail ?? "",

                    EmailTo = x.OwnerEmail ?? "",

                    EmailCc = BuildCc(
                        x.ThirdPartyEmail,
                        x.AdminEmail),

                    PdfPath = x.PdfPath ?? "",
                    AppealPackZipPath =
                        x.AppealPackPath ?? "",
                  

                    Status = x.Status ?? "Pending",
                    ErrorMessage = x.EmailError ?? ""
                })
                .ToList();

            return new ThirdPartyAppealSendEmailVm
            {
                NoticeSettingsId = settings.Id,
                RollId = settings.RollId,

                RollShortCode =
                    await GetRollShortCodeAsync(
                        settings.RollId,
                        ct),

                ValuationPeriod =
                    settings.ValuationPeriodCode ??
                    settings.RollName ??
                    "",

                Notice =
                    "CLA-THIRD PARTY APPLICATION NOTICE",

                TotalPrinted = totalPrinted,
                TotalReadyToSend = totalReady,
                TotalSent = totalSent,
                TotalFailed = totalFailed,
                TotalNoOwnerEmail = totalNoOwnerEmail,

                Groups = groups,
                Items = items
            };
        }

        public async Task<ThirdPartyAppealEmailResultVm> SendAsync(
            Guid key,
            string sentBy,
            CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            if (settings.Notice != NoticeKind.CLA_TPA)
            {
                throw new InvalidOperationException(
                    "This email service is only for CLA Third-Party Application notices.");
            }

            var qaApproved = await _db.NoticeQaRuns
                .AsNoTracking()
                .AnyAsync(x =>
                    x.WorkflowKey == key &&
                    x.Status == "Approved",
                    ct);

            if (!qaApproved)
            {
                throw new InvalidOperationException(
                    "CLA QA must be approved before sending notices.");
            }

            var query = await BuildNoticeQueryAsync(
                settings,
                tracking: true,
                ct);

            var notices = await query
                .Where(x =>
                    x.Status == "Printed" ||
                    x.Status == "Email-Failed")
                .OrderBy(x => x.ClaNumber)
                .ToListAsync(ct);

            var result = new ThirdPartyAppealEmailResultVm
            {
                Total = notices.Count
            };

            foreach (var notice in notices)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(
                            notice.OwnerEmail))
                    {
                        notice.Status = "No-Owner-Email";
                        notice.EmailError =
                            "Owner email is missing.";
                        notice.UpdatedAtUtc =
                            DateTime.UtcNow;
                        notice.UpdatedBy = sentBy;

                        result.Skipped++;

                        await _db.SaveChangesAsync(ct);
                        continue;
                    }

                    ValidateReadyToSend(notice);

                    var sentAt = DateTime.UtcNow;

                    var subject = BuildSubject(
                        settings,
                        notice);

                    var body = BuildBody(notice);
                    var cc = BuildCc(
                        notice.ThirdPartyEmail,
                        notice.AdminEmail);

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
                    //notice.EmlPath = emlPath;

                    notice.SentAtUtc = sentAt;
                    notice.SentBy = sentBy;
                    notice.Status = "Sent";
                    notice.EmailError = null;
                    notice.UpdatedAtUtc = sentAt;
                    notice.UpdatedBy = sentBy;

                    result.Sent++;
                }
                catch (Exception ex)
                {
                    notice.Status = "Email-Failed";
                    notice.EmailError = ex.Message;
                    notice.UpdatedAtUtc = DateTime.UtcNow;
                    notice.UpdatedBy = sentBy;

                    result.Failed++;
                }

                await _db.SaveChangesAsync(ct);
            }

            return result;
        }

        public async Task<ThirdPartyAppealEmailProgressVm>
            GetProgressAsync(
                Guid key,
                CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            var query = await BuildNoticeQueryAsync(
                settings,
                tracking: false,
                ct);

            var total = await query.CountAsync(ct);

            var sent = await query.CountAsync(
                x => x.Status == "Sent",
                ct);

            var failed = await query.CountAsync(
                x => x.Status == "Email-Failed",
                ct);

            var skipped = await query.CountAsync(
                x => x.Status == "No-Owner-Email",
                ct);

            var pending = await query.CountAsync(
                x =>
                    x.Status == "Printed" ||
                    x.Status == "Email-Failed",
                ct);

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
            ClaThirdPartyApplicationNotice notice,
            string subject,
            string body,
            string cc,
            CancellationToken ct)
        {
            var smtpHost =
                _config["Email:Smtp:Host"];

            var smtpPort =
                _config.GetValue<int?>(
                    "Email:Smtp:Port") ?? 25;

            var enableSsl =
                _config.GetValue<bool?>(
                    "Email:Smtp:EnableSsl") ?? false;

            var smtpUser =
                _config["Email:Smtp:Username"];

            var smtpPassword =
                _config["Email:Smtp:Password"];

            var fromEmail =
                _config["Email:FromAddress"] ??
                "Propertyinfo@Joburg.org.za";

            var fromName =
                _config["Email:FromName"] ??
                "City of Johannesburg Valuation Services";

            if (string.IsNullOrWhiteSpace(smtpHost))
            {
                throw new InvalidOperationException(
                    "Email:Smtp:Host is missing in appsettings.");
            }

            using var message = new MailMessage
            {
                From = new MailAddress(
                    fromEmail,
                    fromName),

                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };

            message.To.Add(notice.OwnerEmail!);

            foreach (var email in SplitEmails(cc))
            {
                message.CC.Add(email);
            }

            message.Attachments.Add(
                new Attachment(notice.PdfPath!));

            message.Attachments.Add(
                new Attachment(
                    notice.AppealPackPath!));

            using var smtp = new SmtpClient(
                smtpHost,
                smtpPort)
            {
                EnableSsl = enableSsl,
                DeliveryMethod =
                    SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(smtpUser))
            {
                smtp.Credentials =
                    new NetworkCredential(
                        smtpUser,
                        smtpPassword);
            }
            else
            {
                smtp.UseDefaultCredentials = false;
            }

            await smtp.SendMailAsync(
                message,
                ct);
        }

        private static void ValidateReadyToSend(
            ClaThirdPartyApplicationNotice notice)
        {
            if (string.IsNullOrWhiteSpace(
                    notice.PdfPath) ||
                !File.Exists(notice.PdfPath))
            {
                throw new InvalidOperationException(
                    $"CLA formal notice PDF is missing for '{notice.ClaNumber}'.");
            }

            if (string.IsNullOrWhiteSpace(
                    notice.AppealPackPath) ||
                !File.Exists(
                    notice.AppealPackPath))
            {
                throw new InvalidOperationException(
                    $"Appeal-pack ZIP is missing for '{notice.ClaNumber}'.");
            }
        }

        private static string BuildSubject(
            NoticeSettings settings,
            ClaThirdPartyApplicationNotice notice)
        {
            var period = FirstNonEmpty(
                notice.ValuationPeriod,
                settings.RollName,
                settings.ValuationPeriodCode,
                "GENERAL VALUATION ROLL 2023")
                .ToUpperInvariant();

            var property =
                FirstNonEmpty(
                    notice.PropertyDescription,
                    notice.PremiseId,
                    notice.ClaNumber)
                .ToUpperInvariant();

            return
                $"CLA THIRD-PARTY APPLICATION NOTICE FOR THE {period} – {property}";
        }

        private static string BuildBody(
            ClaThirdPartyApplicationNotice notice)
        {
            var ownerName = FirstNonEmpty(
                notice.OwnerName,
                "Client");

            var adminContact = FirstNonEmpty(
                notice.AdminEmail,
                "valuationenquiries@joburg.org.za");

            return
$@"Dear {ownerName},

CLA Reference: {notice.ClaNumber}
Property Description: {notice.PropertyDescription}
Premise ID: {notice.PremiseId}

Please be advised that the City of Johannesburg has received a third-party application relating to the above-mentioned property.

Attached are:
1. The CLA third-party application formal notice.
2. The supporting appeal-pack ZIP file.

Please review the attached documents and submit any representations by the closing date stated in the formal notice.

For further enquiries or assistance, contact {adminContact} and valuationenquiries@joburg.org.za.

Yours faithfully,
Valuation Appeal Board Secretariat
City of Johannesburg";
        }

        private static string BuildCc(
            string? thirdPartyEmail,
            string? adminEmail)
        {
            var emails = new List<string>();

            if (!string.IsNullOrWhiteSpace(
                    thirdPartyEmail))
            {
                emails.Add(
                    thirdPartyEmail.Trim());
            }

            if (!string.IsNullOrWhiteSpace(
                    adminEmail))
            {
                emails.Add(
                    adminEmail.Trim());
            }

            emails.Add(
                "valuationenquiries@joburg.org.za");

            return string.Join(
                ";",
                emails.Distinct(
                    StringComparer.OrdinalIgnoreCase));
        }

        private string SaveEmailEvidence(
            NoticeSettings settings,
            ClaThirdPartyApplicationNotice notice,
            string subject,
            string body,
            string cc,
            string sentBy,
            DateTime sentAtUtc)
        {
            if (string.IsNullOrWhiteSpace(
                    notice.ClaNumber))
            {
                throw new InvalidOperationException(
                    "CLA number is missing.");
            }

            var rootKey = ResolveRootKey(
                notice.RollShortCode,
                settings.Roll.ToString());

            var appealRoot =
                _config[
                    $"Storage:AppealRootsByShortCode:{rootKey}"];

            if (string.IsNullOrWhiteSpace(appealRoot))
            {
                throw new InvalidOperationException(
                    $"Appeal root path is missing in appsettings for key '{rootKey}'.");
            }

            var pdfFolderName =
                _config[
                    "Storage:CLAThirdPartyAppealApplication:PdfFolderName"]
                ?? "CLA-THIRD-PARTY APPLICATION";

            var emailCopyFolderName =
                _config[
                    "Storage:CLAThirdPartyAppealApplication:EmailCopyFolderName"]
                ?? "CLA EMAIL COPIES";

            var folder = Path.Combine(
                appealRoot,
                SafeFile(notice.ClaNumber),
                pdfFolderName.Trim(),
                emailCopyFolderName.Trim());

            Directory.CreateDirectory(folder);

            var fileName =
                $"email_{SafeFile(notice.ClaNumber)}_" +
                $"{SafeFile(notice.PropertyDescription)}_" +
                $"CLA_Third_Party_Application_" +
                $"{sentAtUtc:yyyyMMdd_HHmmss}.eml";

            var path = Path.Combine(
                folder,
                fileName);

            var fromEmail =
                _config["Email:FromAddress"] ??
                "Propertyinfo@Joburg.org.za";

            var fromName =
                _config["Email:FromName"] ??
                "City of Johannesburg Valuation Services";

            var base64Body = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(body),
                Base64FormattingOptions.InsertLineBreaks);

            var builder = new StringBuilder();

            builder.AppendLine(
                $"From: \"{EscapeHeader(fromName)}\" <{fromEmail}>");

            builder.AppendLine(
                $"To: {notice.OwnerEmail}");

            builder.AppendLine(
                $"Cc: {cc}");

            builder.AppendLine(
                $"Subject: {EncodeHeader(subject)}");

            builder.AppendLine(
                $"Date: {FormatEmailDate(new DateTimeOffset(sentAtUtc))}");

            builder.AppendLine(
                $"Message-ID: <{Guid.NewGuid():N}@joburg.org.za>");

            builder.AppendLine("MIME-Version: 1.0");
            builder.AppendLine(
                "Content-Type: text/plain; charset=\"utf-8\"");
            builder.AppendLine(
                "Content-Transfer-Encoding: base64");
            builder.AppendLine(
                "X-Notice-Type: CLA Third-Party Application");
            builder.AppendLine(
                "X-CLA-Number: " +
                EscapeHeader(notice.ClaNumber));

            builder.AppendLine(
                "X-Sent-By: " +
                EscapeHeader(sentBy));

            builder.AppendLine();
            builder.AppendLine(base64Body);

            File.WriteAllText(
                path,
                builder.ToString(),
                Encoding.ASCII);

            return path;
        }

        private async Task<IQueryable<ClaThirdPartyApplicationNotice>>
            BuildNoticeQueryAsync(
                NoticeSettings settings,
                bool tracking,
                CancellationToken ct)
        {
            IQueryable<ClaThirdPartyApplicationNotice> query =
                tracking
                    ? _db.ClaThirdPartyApplicationNotices
                    : _db.ClaThirdPartyApplicationNotices
                        .AsNoTracking();

            query = query.Where(x => x.IsActive);

            var hasLinkedRows = await query.AnyAsync(
                x => x.NoticeSettingsId == settings.Id,
                ct);

            return hasLinkedRows
                ? query.Where(
                    x => x.NoticeSettingsId == settings.Id)
                : query.Where(x =>
                    x.NoticeSettingsId == null ||
                    x.NoticeSettingsId == 0);
        }

        private async Task<NoticeSettings> GetSettingsAsync(
            Guid key,
            CancellationToken ct)
        {
            return await _db.NoticeSettings
                .FirstOrDefaultAsync(
                    x =>
                        x.ApprovalKey == key ||
                        x.WorkflowKey == key,
                    ct)
                ?? throw new InvalidOperationException(
                    "Workflow settings were not found.");
        }

        private async Task<string> GetRollShortCodeAsync(
            int rollId,
            CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.RollId == rollId,
                    ct);

            return roll?.ShortCode ?? "";
        }

        private static string NormalizeGroup(
            string? propertyType)
        {
            if (string.IsNullOrWhiteSpace(
                    propertyType))
            {
                return "Unspecified";
            }

            if (propertyType.Equals(
                    "Multi",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Multipurpose";
            }

            return propertyType.Trim();
        }

        private static IEnumerable<string> SplitEmails(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Enumerable.Empty<string>();
            }

            return value
                .Split(
                    new[] { ';', ',' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x));
        }

        private static string ResolveRootKey(
            string? recordShortCode,
            string? settingsRoll)
        {
            var value = FirstNonEmpty(
                    recordShortCode,
                    settingsRoll,
                    "GV23")
                .Replace(" ", "")
                .ToUpperInvariant();

            return value switch
            {
                "SUPP1" => "SUPP 1",
                "SUPP2" => "SUPP 2",
                "SUPP3" => "SUPP 3",
                _ => "GV23"
            };
        }

        private static string FirstNonEmpty(
            params string?[] values)
        {
            return values
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x))
                ?.Trim()
                ?? "";
        }

        private static string SafeFile(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var invalid =
                Path.GetInvalidFileNameChars();

            var cleaned = new string(
                value.Select(character =>
                    invalid.Contains(character)
                        ? '_'
                        : character)
                    .ToArray());

            if (cleaned.Length > 120)
            {
                cleaned =
                    cleaned[..120].Trim();
            }

            return cleaned;
        }

        private static string EscapeHeader(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\"", "'");
        }

        private static string EncodeHeader(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            if (value.All(character =>
                    character <= 127))
            {
                return value
                    .Replace("\r", "")
                    .Replace("\n", "");
            }

            return
                "=?utf-8?B?" +
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(value)) +
                "?=";
        }

        private static string FormatEmailDate(
            DateTimeOffset value)
        {
            var offset = value.Offset;
            var sign =
                offset < TimeSpan.Zero
                    ? "-"
                    : "+";

            offset = offset.Duration();

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}, {1:00} {2} {3:0000} {4:00}:{5:00}:{6:00} {7}{8:00}{9:00}",
                value.ToString(
                    "ddd",
                    System.Globalization.CultureInfo.InvariantCulture),
                value.Day,
                value.ToString(
                    "MMM",
                    System.Globalization.CultureInfo.InvariantCulture),
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

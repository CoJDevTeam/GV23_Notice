using GV23_Notice.Domain.Email;
using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace GV23_Notice.Services.Stats
{
    public sealed class NoticeStatsEmailService : INoticeStatsEmailService
    {
        private readonly EmailOptions _emailOpt;
        private readonly ILogger<NoticeStatsEmailService> _log;

        public NoticeStatsEmailService(
            IOptions<EmailOptions> emailOpt,
            ILogger<NoticeStatsEmailService> log)
        {
            _emailOpt = emailOpt.Value;
            _log = log;
        }

        public async Task SendStatsEmailAsync(
            string toEmails,
            string? ccEmails,
            string subject,
            string htmlBody,
            string attachmentPath,
            CancellationToken ct,
            string? fromAddress = null,
            string? fromName = null)
        {
            if (string.IsNullOrWhiteSpace(toEmails))
                throw new InvalidOperationException("Please enter at least one stakeholder email address.");

            if (string.IsNullOrWhiteSpace(_emailOpt.Smtp?.Host))
                throw new InvalidOperationException("Email:Smtp:Host is not configured.");

            if (string.IsNullOrWhiteSpace(attachmentPath) || !File.Exists(attachmentPath))
                throw new FileNotFoundException($"Stats Excel file not found: {attachmentPath}");

            var senderAddress = string.IsNullOrWhiteSpace(fromAddress)
                ? _emailOpt.FromAddress
                : fromAddress.Trim();

            var senderName = string.IsNullOrWhiteSpace(fromName)
                ? _emailOpt.FromName
                : fromName.Trim();

            using var msg = new MailMessage
            {
                From = new MailAddress(senderAddress, senderName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            AddAddresses(msg.To, toEmails);

            if (!string.IsNullOrWhiteSpace(ccEmails))
                AddAddresses(msg.CC, ccEmails);

            var excelBytes = await File.ReadAllBytesAsync(attachmentPath, ct);

            var attachment = new Attachment(
                new MemoryStream(excelBytes),
                Path.GetFileName(attachmentPath),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

            msg.Attachments.Add(attachment);

            using var smtp = new SmtpClient(_emailOpt.Smtp.Host, _emailOpt.Smtp.Port)
            {
                EnableSsl = _emailOpt.Smtp.EnableSsl
            };

            if (_emailOpt.Smtp.UseDefaultCredentials || _emailOpt.UseDefaultCredentials)
            {
                smtp.UseDefaultCredentials = true;
            }
            else
            {
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new System.Net.NetworkCredential(
                    _emailOpt.Smtp.Username ?? "",
                    _emailOpt.Smtp.Password ?? "");
            }

            await Task.Run(() => smtp.Send(msg), ct);

            _log.LogInformation(
                "Notice stats email sent to {ToEmails}. Attachment: {Attachment}",
                toEmails,
                attachmentPath);
        }

        private static void AddAddresses(MailAddressCollection collection, string addresses)
        {
            foreach (var address in addresses.Split(';', ',', StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = address.Trim();

                if (!string.IsNullOrWhiteSpace(clean))
                    collection.Add(new MailAddress(clean));
            }
        }
    }
}
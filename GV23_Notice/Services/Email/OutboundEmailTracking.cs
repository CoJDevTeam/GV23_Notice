using System.Net.Mail;

namespace GV23_Notice.Services.Email
{
    public static class OutboundEmailTracking
    {
        public static void Apply(
            MailMessage message,
            IConfiguration configuration,
            string? trackingReference = null)
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(configuration);

            var section = configuration.GetSection("Email:Tracking");

            if (!(section.GetValue<bool?>("Enabled") ?? false))
                return;

            var mailbox =
                section["TrackingMailbox"]?.Trim()
                ?? configuration["Email:FromAddress"]?.Trim();

            if (section.GetValue<bool?>("RequestDeliveryReceipt") ?? true)
            {
                message.DeliveryNotificationOptions =
                    DeliveryNotificationOptions.OnSuccess |
                    DeliveryNotificationOptions.OnFailure |
                    DeliveryNotificationOptions.Delay;
            }

            if ((section.GetValue<bool?>("RequestReadReceipt") ?? false) &&
                !string.IsNullOrWhiteSpace(mailbox))
            {
                SetHeader(message, "Disposition-Notification-To", mailbox);
                SetHeader(message, "Return-Receipt-To", mailbox);
            }

            SetHeader(message, "X-GV23-Tracking-Id", Guid.NewGuid().ToString("N"));
            SetHeader(message, "X-GV23-Tracking-Requested-At-Utc", DateTime.UtcNow.ToString("O"));

            if (!string.IsNullOrWhiteSpace(trackingReference))
                SetHeader(message, "X-GV23-Tracking-Reference", trackingReference);
        }

        private static void SetHeader(
            MailMessage message,
            string name,
            string value)
        {
            var clean = value
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Trim();

            message.Headers.Remove(name);
            message.Headers.Add(name, clean);
        }
    }
}

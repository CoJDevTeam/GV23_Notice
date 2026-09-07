namespace GV23_Notice.Domain.Email
{
    public sealed class EmailOptions
    {
        public const string SectionName = "Email";

        public string FromAddress { get; set; } = "";

        public string FromName { get; set; } = "";

        public string CcAddress { get; set; } = "";

        public string CcName { get; set; } = "";

        public string BaseUrl { get; set; } = "";

        public List<string> ApprovalRecipients { get; set; } = new();

        public List<string> ApprovalCcRecipients { get; set; } = new();

        public List<string> CorrectionRecipients { get; set; } = new();

        public List<string> CorrectionCcRecipients { get; set; } = new();

        public SmtpOptions Smtp { get; set; } = new();

        public LimitsOptions Limits { get; set; } = new();

        public TemplatesOptions Templates { get; set; } = new();

        public TestModeOptions TestMode { get; set; } = new();

        public TrackingOptions Tracking { get; set; } = new();

        public EnquiriesOptions Enquiries { get; set; } = new();

        public SignOffOptions SignOff { get; set; } = new();

        public string PortalUrl { get; set; } = "";

        public bool UseDefaultCredentials { get; set; } = false;

        public bool UserDefaultCredentials
        {
            get => UseDefaultCredentials;
            set => UseDefaultCredentials = value;
        }

        public sealed class TestModeOptions
        {
            public bool Enabled { get; set; }

            public string Recipient { get; set; } = "";

            public bool PreserveOriginalRecipient { get; set; } = true;

            public bool UseOriginalRecipientInArchiveFileName { get; set; } = true;
        }

        public sealed class TrackingOptions
        {
            public bool Enabled { get; set; }

            public bool RequestDeliveryReceipt { get; set; }

            public bool RequestReadReceipt { get; set; }

            public string TrackingMailbox { get; set; } = "";
        }

        public sealed class SmtpOptions
        {
            public string Host { get; set; } = "";

            public int Port { get; set; } = 587;

            public bool EnableSsl { get; set; } = true;

            public string Username { get; set; } = "";

            public string Password { get; set; } = "";

            public bool UseDefaultCredentials { get; set; }
        }

        public sealed class LimitsOptions
        {
            public int MaxSendPerBatch { get; set; } = 2000;

            public int DelayMsBetweenSends { get; set; }
        }

        public sealed class TemplatesOptions
        {
            public string FooterHtml { get; set; } = "";

            public string SignatureHtml { get; set; } = "";
        }

        public sealed class EnquiriesOptions
        {
            public string Tel1 { get; set; } = "";

            public string Tel2 { get; set; } = "";

            public string Email { get; set; } = "";
        }

        public sealed class SignOffOptions
        {
            public string Line1 { get; set; } = "";

            public string Line2 { get; set; } = "";

            public string Line3 { get; set; } = "";
        }
    }
}
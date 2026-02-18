namespace GV23_Notice.Domain.Email
{
    public sealed class EmailOptions
    {
        public string FromAddress { get; set; } = "";
        public string FromName { get; set; } = "";

        public SmtpOptions Smtp { get; set; } = new();
        public LimitsOptions Limits { get; set; } = new();
        public TemplatesOptions Templates { get; set; } = new();

        public sealed class SmtpOptions
        {
            public string Host { get; set; } = "";
            public int Port { get; set; } = 587;
            public bool EnableSsl { get; set; } = true;
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
        }

        public sealed class LimitsOptions
        {
            public int MaxSendPerBatch { get; set; } = 2000;
            public int DelayMsBetweenSends { get; set; } = 0;
        }

        public sealed class TemplatesOptions
        {
            public string FooterHtml { get; set; } = "";
            public string SignatureHtml { get; set; } = "";
        }
    }
}

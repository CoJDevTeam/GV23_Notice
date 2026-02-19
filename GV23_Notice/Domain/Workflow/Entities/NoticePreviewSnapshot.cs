namespace GV23_Notice.Domain.Workflow.Entities
{
    public sealed class NoticePreviewSnapshot
    {
        public long Id { get; set; }

        public int SettingsId { get; set; }
        public NoticeSettings Settings { get; set; } = null!;

        public NoticeKind Notice { get; set; }

        // "Default", "S52Review", "InvalidObjection", etc
        public string Variant { get; set; } = "Default";

        // "single" | "multi" | "split"
        public string Mode { get; set; } = "single";

        public string EmailSubject { get; set; } = "";
        public string EmailBodyHtml { get; set; } = "";

        // Optional: only if you decide to store PDF bytes
        public byte[]? PdfBytes { get; set; }

        // for versioning
        public bool IsApprovedSnapshot { get; set; }

        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        
        public string UiMode { get; set; } = "single";   // single|multi|split

        public string RecipientName { get; set; } = "";
        public string RecipientEmail { get; set; } = "";

     

        // Option A (recommended): store PDF bytes for exact audit replay
      
        public string PdfFileName { get; set; } = "";

    }
}

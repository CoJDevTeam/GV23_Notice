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


        public int? NoticeBatchId { get; set; }          // link to batch
        public int? NoticeRunLogId { get; set; }         // link to runlog

        public string? ObjectionNo { get; set; }
        public string? AppealNo { get; set; }
        public string? PremiseId { get; set; }

        // For double print rules
        public string? ObjectorType { get; set; }        // Representative/Third_Party/Owner-Rep...
        public string? CopyRole { get; set; }            // "OWNER" or "REP" (or null)

        public string? PropertyDesc { get; set; }        // for filename/subject, etc. Optional but recommended to avoid issues with missing data in related entities at print time.    

    }
}




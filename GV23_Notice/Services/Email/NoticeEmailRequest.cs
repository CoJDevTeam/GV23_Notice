using GV23_Notice.Domain.Workflow;
using static QuestPDF.Helpers.Colors;

namespace GV23_Notice.Services.Email
{
    public sealed class NoticeEmailRequest
    {
        public NoticeKind Notice { get; set; }

        // roll info (common)
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";         // display name
        public string RollDisplayName { get; set; } = "";  // optional if you use wording

        // recipient
        public string RecipientName { get; set; } = "";    // e.g. Addr1 or “Client”
        public string RecipientEmail { get; set; } = "";

        // Multi vs Single email behaviour (list of property descs + refs)
        public bool IsMulti { get; set; }

        public List<NoticeEmailPropertyItem> Items { get; set; } = new();

        // === S49 specific ===
        public DateOnly? InspectionStart { get; set; }
        public DateOnly? InspectionEnd { get; set; }
        public DateOnly? ExtendedEnd { get; set; }
        public string? FinancialYearsText { get; set; }
        public string? RollTypeText { get; set; }         // from RollWording

        // === S52 specific ===
        public bool? IsSection52Review { get; set; }       // true=Review, false=Appeal

        // === Invalid specific ===
        public InvalidNoticeKind? InvalidKind { get; set; } // InvalidOmission / InvalidObjection

        public DateTime? S51SubmissionsCloseDate { get; set; }
        public string? S51PortalUrl { get; set; } = "https://objections.joburg.org.za";
        public string? S51Section51Pin { get; set; }
        public string? S51ObjectionNo { get; set; }
    }

    public sealed class NoticeEmailPropertyItem
    {
        public string PropertyDesc { get; set; } = "";
        public string? ObjectionNo { get; set; }
        public string? AppealNo { get; set; }
    }

    public enum InvalidNoticeKind
    {
        InvalidOmission = 1,
        InvalidObjection = 2
    }
}

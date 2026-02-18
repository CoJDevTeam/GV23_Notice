namespace GV23_Notice.Domain.Workflow.Entities
{
    public sealed class NoticeTemplateApproval
    {
        public int Id { get; set; }

        // Link to Step1 settings (versioned)
        public int NoticeSettingsId { get; set; }
        public NoticeSettings? NoticeSettings { get; set; }

        // Step2 approval status
        public bool IsApproved { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }

        // If you ever “unapprove”
        public bool IsRevoked { get; set; }
        public string? RevokedBy { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public string? RevokedReason { get; set; }

        // Audit
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = "";
    }
}

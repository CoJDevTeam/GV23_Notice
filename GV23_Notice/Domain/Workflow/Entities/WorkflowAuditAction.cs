namespace GV23_Notice.Domain.Workflow.Entities
{
    public enum WorkflowAuditAction
    {
        Step2Viewed = 200,
        Step2Approved = 210,
        Step2CorrectionRequested = 220,

        CorrectionEmailSent = 230,
        CorrectionTicketCreated = 240
    }

    public sealed class NoticeWorkflowAuditLog
    {
        public long Id { get; set; }

        public int NoticeSettingsId { get; set; }
        public NoticeSettings? NoticeSettings { get; set; }

        public int RollId { get; set; }
        public NoticeKind Notice { get; set; }
        public int Version { get; set; }

        public WorkflowAuditAction Action { get; set; }

        public string PerformedBy { get; set; } = "";
        public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;

        public string? Notes { get; set; }
        public string? MetaJson { get; set; } // store extra info (email recipients, etc.)
    }
}

using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Models.DTOs
{
    public sealed class NoticeSettingsAudit
    {
        public long Id { get; set; }

        public int SettingsId { get; set; }
        public NoticeSettings Settings { get; set; } = null!;

        public string Step { get; set; } = "";        // "Step1Save", "Step1Confirm", "Step1Approve", ...
        public string Action { get; set; } = "";      // "SAVE_DRAFT", "CONFIRM", "APPROVE", "REQUEST_CORRECTION"
        public string PerformedBy { get; set; } = "";
        public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;

        public string? Comment { get; set; }

        // ✅ Store a JSON snapshot of important values at that moment
        public string? SnapshotJson { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public class NoticeApprovalLog
    {
        public int Id { get; set; }

        public int NoticeSettingsId { get; set; }
        public NoticeSettings NoticeSettings { get; set; } = null!;

        public ApprovalAction Action { get; set; }

        [MaxLength(256)]
        public string PerformedBy { get; set; } = "";

        public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }
}

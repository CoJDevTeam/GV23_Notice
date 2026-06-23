

using GV23_Notice.Domain.Workflow;
using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public sealed class NoticeQaRun
    {
        public int Id { get; set; }

        public Guid WorkflowKey { get; set; }

        public int NoticeSettingsId { get; set; }
        public NoticeSettings NoticeSettings { get; set; } = null!;

        public int RollId { get; set; }

        public NoticeKind Notice { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = "Open";
        // Open | Approved | Failed

        [MaxLength(256)]
        public string CreatedBy { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? ApprovedBy { get; set; }

        public DateTime? ApprovedAtUtc { get; set; }

        [MaxLength(2000)]
        public string? Comment { get; set; }

        public ICollection<NoticeQaItem> Items { get; set; } = new List<NoticeQaItem>();
    }
}
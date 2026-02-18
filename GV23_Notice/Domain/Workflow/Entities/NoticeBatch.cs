using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public class NoticeBatch
    {
        public int Id { get; set; }

        public int NoticeSettingsId { get; set; }
        public NoticeSettings NoticeSettings { get; set; } = null!;

        [MaxLength(80)]
        public string BatchName { get; set; } = "";

        [MaxLength(256)]
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Copy of version details for safety/reporting
        public RollCode Roll { get; set; }
        public NoticeKind Notice { get; set; }
        public int SettingsVersionUsed { get; set; }

        // Optional filter snapshot (especially for S52 bulk)
        public DateTime? BulkFromDate { get; set; }
        public DateTime? BulkToDate { get; set; }

        public ICollection<NoticeRunLog> Runs { get; set; } = new List<NoticeRunLog>();
    }
}

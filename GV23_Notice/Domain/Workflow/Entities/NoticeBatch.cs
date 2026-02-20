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


        public string? BatchKind { get; set; } // e.g. "S52 Bulk" or "S53 Batch"

        public Guid WorkflowKey { get; set; }

        public int RollId { get; set; }

        public BatchMode Mode { get; set; }

        public string? Version { get; set; }

        public ICollection<NoticeRunLog> Runs { get; set; } = new List<NoticeRunLog>();

      


        public DateTime BatchDate { get; set; } = DateTime.Today;

      

        public int NumberOfRecords { get; set; }

        // backup / approval details
        public bool IsApproved { get; set; }
        [MaxLength(256)]
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
    }
}

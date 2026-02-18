using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public sealed class S49BatchRun
    {
        public int Id { get; set; }

        public int SettingsId { get; set; }         // NoticeSettings.Id
        public int RollId { get; set; }

        [MaxLength(50)]
        public string BatchName { get; set; } = ""; // e.g. S49N_Batch_001

        public int TargetSize { get; set; } = 500;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(150)]
        public string CreatedBy { get; set; } = "";

        public int PickedPremiseCount { get; set; }
        public int SentCount { get; set; }
        public int FailedCount { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "Created"; // Created, Running, Completed

        public List<S49BatchItem> Items { get; set; } = new();
    }

    public sealed class S49BatchItem
    {
        public int Id { get; set; }
        public int BatchRunId { get; set; }
        public S49BatchRun? BatchRun { get; set; }

        [MaxLength(50)]
        public string PremiseId { get; set; } = "";

        [MaxLength(250)]
        public string? Email { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "Picked"; // Picked, Sent, Failed

        public DateTime? SentAtUtc { get; set; }

        [MaxLength(4000)]
        public string? Error { get; set; }

        // Optional helpful snapshot
        public bool IsSplit { get; set; }
        public int RowCount { get; set; }
    }
}

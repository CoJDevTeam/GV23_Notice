using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public class NoticeRunLog
    {
        public int Id { get; set; }

        public int NoticeBatchId { get; set; }
        public NoticeBatch NoticeBatch { get; set; } = null!;

        [MaxLength(80)]
        public string? ObjectionNo { get; set; }

        [MaxLength(80)]
        public string? AppealNo { get; set; }

        [MaxLength(50)]
        public string? PremiseId { get; set; }

        [MaxLength(320)]
        public string? RecipientEmail { get; set; }

        [MaxLength(256)]
        public string? RecipientName { get; set; }

        [MaxLength(512)]
        public string? PropertyDesc { get; set; }

        [MaxLength(260)]
        public string? PdfPath { get; set; }

        [MaxLength(260)]
        public string? EmlPath { get; set; }

        public RunStatus Status { get; set; } = RunStatus.Generated;

        [MaxLength(2000)]
        public string? ErrorMessage { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? SentAtUtc { get; set; }
    }
}
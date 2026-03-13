using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public class NoticeDownloadLog
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string DownloadedBy { get; set; } = "";

        public DateTime DownloadedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>PropertyDesc | ObjectionNo | AppealNo</summary>
        [Required, MaxLength(20)]
        public string SearchMode { get; set; } = "";

        [Required, MaxLength(500)]
        public string SearchTerm { get; set; } = "";

        [MaxLength(80)]
        public string? ObjectionNo { get; set; }

        [MaxLength(80)]
        public string? AppealNo { get; set; }

        [MaxLength(512)]
        public string? PropertyDesc { get; set; }

        public int FileCount { get; set; }

        [MaxLength(260)]
        public string? ZipFileName { get; set; }
    }
}

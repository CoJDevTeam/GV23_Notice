using GV23_Notice.Services.Preview;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public sealed class NoticeStep2CorrectionRequest
    {
        [Key]
        public int Id { get; set; }

        public int NoticeSettingsId { get; set; }

        public int RollId { get; set; }

        public NoticeKind Notice { get; set; }

        public PreviewVariant Variant { get; set; }

        public PreviewMode Mode { get; set; }

        [MaxLength(50)]
        public string Version { get; set; } = "";

        // What the manager asked to correct
        [MaxLength(2000)]
        public string Reason { get; set; } = "";

        // Snapshot at time of request (for audit)
        [MaxLength(500)]
        public string EmailSubjectSnapshot { get; set; } = "";

        [Column(TypeName = "nvarchar(max)")]
        public string EmailBodyHtmlSnapshot { get; set; } = "";

        [MaxLength(255)]
        public string PdfFileName { get; set; } = "";

        [MaxLength(128)]
        public string PdfSha256 { get; set; } = "";

        [MaxLength(200)]
        public string RequestedBy { get; set; } = "";

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "nvarchar(max)")]
        public string SettingsJsonSnapshot { get; set; } = "";
    }
}

using GV23_Notice.Services.Preview;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public sealed class NoticeStep2Snapshot
    {
        [Key]
        public int Id { get; set; }

        public int NoticeSettingsId { get; set; }

        public int RollId { get; set; }

        public NoticeKind Notice { get; set; }

        public PreviewVariant Variant { get; set; }

        public PreviewMode Mode { get; set; }

        // The version from NoticeSettings (handy to track changes)
        [MaxLength(50)]
        public string Version { get; set; } = "";

        // Snapshot content (what the user saw/approved)
        [MaxLength(500)]
        public string EmailSubjectSnapshot { get; set; } = "";

        [Column(TypeName = "nvarchar(max)")]
        public string EmailBodyHtmlSnapshot { get; set; } = "";

        // PDF snapshot — bytes stored for exact replay at print time
        [Column(TypeName = "varbinary(max)")]
        public byte[]? PdfBytes { get; set; }

        [MaxLength(255)]
        public string PdfFileName { get; set; } = "";

        [MaxLength(128)]
        public string PdfSha256 { get; set; } = "";

        // Who/when
        [MaxLength(200)]
        public string CreatedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // A lightweight snapshot of Step1 settings can be stored for auditing
        [Column(TypeName = "nvarchar(max)")]
        public string SettingsJsonSnapshot { get; set; } = "";



    }
}
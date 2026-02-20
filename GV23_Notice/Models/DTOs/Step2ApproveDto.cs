using GV23_Notice.Services.Preview;
using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Models.DTOs
{
    public sealed class Step2ApproveDto
    {
        [Required] public int SettingsId { get; set; }
        [Required] public PreviewVariant Variant { get; set; }
        [Required] public PreviewMode Mode { get; set; }
        public string? AppealNo { get; set; } // only needed for S52 preview rebuild
    }

    public sealed class Step2CorrectionDto
    {
        [Required] public int SettingsId { get; set; }
        [Required] public PreviewVariant Variant { get; set; }
        [Required] public PreviewMode Mode { get; set; }
        public string? AppealNo { get; set; }

        [Required, MaxLength(2000)]
        public string Reason { get; set; } = "";
    }
}

using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Rolls
{
    public class RollRegistry
    {
        [Key]
        public int RollId { get; set; }

        public int? LegacyRollId { get; set; }

        [Required, MaxLength(20)]
        public string RollType { get; set; } = ""; // GV / Supp / Query

        [Required, MaxLength(200)]
        public string Name { get; set; } = "";

        [Required, MaxLength(50)]
        public string ShortCode { get; set; } = ""; // "GV23", "SUPP 1", "SUPP 2", ...

        [Required, MaxLength(200)]
        public string SourceDb { get; set; } = ""; // "Objection", "Objection_Supp1", ...

        public bool IsActive { get; set; } = true;

        [Required, MaxLength(200)]
        public string CreatedBy { get; set; } = "";

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        [MaxLength(200)]
        public string? UpdatedBy { get; set; }

        public DateTime? UpdatedOn { get; set; }
    }
}

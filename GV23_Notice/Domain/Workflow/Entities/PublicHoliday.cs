using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    /// <summary>
    /// South African public holidays stored centrally in Notice_DB.
    /// Used by S53 appeal close date calculation.
    /// ValuationPeriodCode links a holiday to a GV valuation period (e.g. "GV23")
    /// so the S53 calculator knows which holidays apply to a given notice run.
    /// </summary>
    public class PublicHoliday
    {
        public int Id { get; set; }

        /// <summary>The actual public holiday date.</summary>
        public DateOnly HolidayDate { get; set; }

        /// <summary>Display name e.g. "Human Rights Day".</summary>
        [MaxLength(150)]
        public required string HolidayName { get; set; }

        /// <summary>Day of week name e.g. "Monday" — informational, auto-derived on insert.</summary>
        [MaxLength(20)]
        public string? HolidayDay { get; set; }

        /// <summary>Calendar year (2023–2026).</summary>
        public int Year { get; set; }

        /// <summary>
        /// Valuation period this holiday falls under e.g. "GV23".
        /// Matches NoticeSettings.ValuationPeriodCode.
        /// NULL = applies to all periods (legacy/imported rows).
        /// </summary>
        [MaxLength(20)]
        public string? ValuationPeriodCode { get; set; }

        /// <summary>Optional note e.g. "Sunday → Monday observed".</summary>
        [MaxLength(300)]
        public string? Note { get; set; }

        /// <summary>Who last updated this row.</summary>
        [MaxLength(256)]
        public string? UpdatedBy { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }
    }
}

using GV23_Notice.Domain.Workflow;
using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class WorkflowStep1Vm
    {
        // ── Core selection (all required) ──────────────────────────────────
        [Required(ErrorMessage = "Roll is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a roll.")]
        public int RollId { get; set; }

        [Required(ErrorMessage = "Notice type is required.")]
        public NoticeKind Notice { get; set; }

        [Required(ErrorMessage = "Batch mode is required.")]
        public BatchMode Mode { get; set; } = BatchMode.Bulk;

        [Required(ErrorMessage = "Valuation Period is required.")]
        public string? ValuationPeriodCode { get; set; }

        // Financial year — validated by checking FinancialYearStart on server
        public DateTime? FinancialYearStart { get; set; }
        public DateTime? FinancialYearEnd { get; set; }
        public string? FinancialYearsText { get; set; }

        // ── Common fields ──────────────────────────────────────────────────
        [DataType(DataType.Date)]
        public DateTime LetterDate { get; set; } = DateTime.Today;

        public bool LetterDateOverridden { get; set; }

        [MaxLength(500)]
        public string? LetterDateOverrideReason { get; set; }

        [MaxLength(500)]
        public string? PortalUrl { get; set; }

        [MaxLength(500)]
        public string? EnquiriesLine { get; set; }

        [DataType(DataType.Date)]
        public DateTime? CityManagerSignDate { get; set; }

        // ── S49 ────────────────────────────────────────────────────────────
        [DataType(DataType.Date)]
        public DateTime? ObjectionStartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ObjectionEndDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ExtensionDate { get; set; }

        public string? ExistingSignaturePath { get; set; }

        // ── S51 ────────────────────────────────────────────────────────────
        [DataType(DataType.Date)]
        public DateTime? EvidenceCloseDate { get; set; }

        // ── S52 ────────────────────────────────────────────────────────────
        [DataType(DataType.Date)]
        public DateTime? BulkFromDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? BulkToDate { get; set; }

        public S52SendMode S52SendMode { get; set; } = S52SendMode.Both;

        // ── S53 ────────────────────────────────────────────────────────────
        [DataType(DataType.Date)]
        public DateTime? BatchDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? AppealCloseDate { get; set; }

        public bool AppealCloseOverridden { get; set; }

        [MaxLength(500)]
        public string? AppealCloseOverrideReason { get; set; }

        public string? ExistingOverrideEvidencePath { get; set; }

        // ── S78 ────────────────────────────────────────────────────────────
        [DataType(DataType.Date)]
        public DateTime? ExtractionDate { get; set; }

        [Range(1, 365)]
        public int? ExtractPeriodDays { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ReviewOpenDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ReviewCloseDate { get; set; }

        // ── IN ─────────────────────────────────────────────────────────────
        public INSendMode INSendMode { get; set; } = INSendMode.Both;

        // ── Meta / display ─────────────────────────────────────────────────
        public int? SettingsId { get; set; }
        public string? RollShortCode { get; set; }

        // Kept for migration compatibility
        public bool? IsInvalidOmission { get; set; }
    }
}
using GV23_Notice.Domain.Workflow;
using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class WorkflowStep1Vm
    {
        // Selection
        [Required]
        public RollCode Roll { get; set; }

        [Required]
        public NoticeKind Notice { get; set; }

        public int RollId { get; set; }
        [Required]
        public BatchMode Mode { get; set; } = BatchMode.Bulk; // default, you can change

        // Common
        [DataType(DataType.Date)]
        public DateTime LetterDate { get; set; } = DateTime.Today;

        public bool LetterDateOverridden { get; set; }

        [MaxLength(500)]
        public string? LetterDateOverrideReason { get; set; }

        // Shared optional fields
        [MaxLength(500)]
        public string? PortalUrl { get; set; }

        [MaxLength(500)]
        public string? EnquiriesLine { get; set; }

        [DataType(DataType.Date)]
        public DateTime? CityManagerSignDate { get; set; }

        // S49
        [DataType(DataType.Date)]
        public DateTime? ObjectionStartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ObjectionEndDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ExtensionDate { get; set; }

        // Signature file will be uploaded in controller (IFormFile)
        public string? ExistingSignaturePath { get; set; }

        // S51 (auto calc)
        [DataType(DataType.Date)]
        public DateTime? EvidenceCloseDate { get; set; } // will be auto-set = LetterDate + 30

        // S52 sub-type + bulk range
        /// <summary>
        /// true  = Section 52 Review (A_UserID = "System_Generated")
        /// false = Appeal Decision
        /// </summary>
        public bool IsSection52Review { get; set; }

        [DataType(DataType.Date)]
        public DateTime? BulkFromDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? BulkToDate { get; set; }

        // S53
        [DataType(DataType.Date)]
        public DateTime? BatchDate { get; set; } // default today

        [DataType(DataType.Date)]
        public DateTime? AppealCloseDate { get; set; }

        public bool AppealCloseOverridden { get; set; }

        [MaxLength(500)]
        public string? AppealCloseOverrideReason { get; set; }

        public string? ExistingOverrideEvidencePath { get; set; }

        // S78
        [DataType(DataType.Date)]
        public DateTime? ExtractionDate { get; set; }

        [Range(1, 365)]
        public int? ExtractPeriodDays { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ReviewOpenDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ReviewCloseDate { get; set; }

        // Used for redirecting to Step2 after approve
        public int? SettingsId { get; set; }


        public string? RollShortCode { get; set; } // optional for display


        public string? ValuationPeriodCode { get; set; } // "GV23"

        public DateTime? FinancialYearStart { get; set; }
        public DateTime? FinancialYearEnd { get; set; }

        // Optional: keep for display
        public string? FinancialYearsText { get; set; }






    }
}

using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public class NoticeSettings
    {
        public int Id { get; set; }

        public RollCode Roll { get; set; }
        public NoticeKind Notice { get; set; }
        public BatchMode Mode { get; set; }

        public int RollId { get; set; }

        // Versioning: increments per Roll+Notice+Mode
        public int Version { get; set; }

        // Common date fields (nullable; per-notice you fill what you need)
        public DateTime LetterDate { get; set; } = DateTime.Today;

        public DateTime? ObjectionStartDate { get; set; }
        public DateTime? ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }

        public DateTime? EvidenceCloseDate { get; set; } // S51 = LetterDate + 30

        // S52 bulk range approval (optional)
        public DateTime? BulkFromDate { get; set; }
        public DateTime? BulkToDate { get; set; }

        // S53
        public DateTime? BatchDate { get; set; } // default today
        public DateTime? AppealCloseDate { get; set; }

        // Optional override for S53 appeal close date
        [MaxLength(500)]
        public string? AppealCloseOverrideReason { get; set; }
        [MaxLength(260)]
        public string? AppealCloseOverrideEvidencePath { get; set; }
        [MaxLength(256)]
        public string? AppealCloseOverrideBy { get; set; }
        public DateTime? AppealCloseOverrideAtUtc { get; set; }

        // S49 signature (required to proceed)
        [MaxLength(260)]
        public string? SignaturePath { get; set; }

        // Shared optional fields for all notices
        [MaxLength(500)]
        public string? PortalUrl { get; set; }
        [MaxLength(500)]
        public string? EnquiriesLine { get; set; }
        public DateTime? CityManagerSignDate { get; set; }

        // Workflow state
        public bool IsConfirmed { get; set; }
        [MaxLength(256)]
        public string? ConfirmedBy { get; set; }
        public DateTime? ConfirmedAtUtc { get; set; }

        public bool IsApproved { get; set; }
        [MaxLength(256)]
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }

        public DateTime? ExtractionDate { get; set; }

        /// <summary>
        /// Extract period in days (e.g. 7, 14, 30). If you later want "Month/Week" style,
        /// we can introduce another enum + unit field.
        /// </summary>
        [Range(1, 365)]
        public int? ExtractPeriodDays { get; set; }

        public DateTime? ReviewOpenDate { get; set; }
        public DateTime? ReviewCloseDate { get; set; }

        // If LetterDate was overridden from Today (Section49 option B)
        public bool LetterDateOverridden { get; set; }
        [MaxLength(500)]
        public string? LetterDateOverrideReason { get; set; }

        // Navigation
        public ICollection<NoticeApprovalLog> ApprovalLogs { get; set; } = new List<NoticeApprovalLog>();
        public ICollection<NoticeBatch> Batches { get; set; } = new List<NoticeBatch>();

        public bool IsStep2Approved { get; set; }        // quick flag for filtering
        public string? Step2ApprovedBy { get; set; }
        public DateTime? Step2ApprovedAtUtc { get; set; }

        public string? FinancialYearsText { get; set; }

        public string? RollName { get; set; }

        public Guid? ApprovalKey { get; set; }

        public string? ApprovedEmailSavedPath { get; set; }

        public string? ApprovedEmailSentAtUtc { get; set; }

        public string? CorrectionEmailSavedPath { get; set; }

        public string? CorrectionEmailSentAtUtc { get; set; }

        public string? ValuationPeriodCode { get; set; }   // e.g. "GV23"
        public DateTime? ValuationPeriodStart { get; set; } // 2023-07-01
        public DateTime? ValuationPeriodEnd { get; set; }   // 2027-06-30

        public DateTime? FinancialYearStart { get; set; }   // 2025-07-01
        public DateTime? FinancialYearEnd { get; set; }     // 2026-06-30
    }
}
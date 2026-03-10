using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class WorkflowStep1SummaryVm
    {
        public int SettingsId { get; set; }
        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public BatchMode Mode { get; set; }
        public int Version { get; set; }

        public bool IsConfirmed { get; set; }
        public bool IsApproved { get; set; }

        public DateTime LetterDate { get; set; }
        public string? PortalUrl { get; set; }
        public string? EnquiriesLine { get; set; }
        public DateTime? CityManagerSignDate { get; set; }

        public DateTime? ObjectionStartDate { get; set; }
        public DateTime? ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }
        public string? SignaturePath { get; set; }

        public DateTime? EvidenceCloseDate { get; set; }

        public DateTime? BulkFromDate { get; set; }
        public DateTime? BulkToDate { get; set; }

        /// <summary>Which S52 sub-type(s) were selected on Step 1.</summary>
        public S52SendMode S52SendMode { get; set; } = S52SendMode.Both;
        /// <summary>Derived: true if Review is included in the send mode.</summary>
        public bool ShowReview => S52SendMode == S52SendMode.ReviewOnly || S52SendMode == S52SendMode.Both;
        /// <summary>Derived: true if Appeal Decision is included in the send mode.</summary>
        public bool ShowAppealDecision => S52SendMode == S52SendMode.AppealDecisionOnly || S52SendMode == S52SendMode.Both;

        public DateTime? BatchDate { get; set; }
        public DateTime? AppealCloseDate { get; set; }
        public string? AppealCloseOverrideReason { get; set; }
        public string? AppealCloseOverrideEvidencePath { get; set; }

        public DateTime? ExtractionDate { get; set; }
        public int? ExtractPeriodDays { get; set; }
        public DateTime? ReviewOpenDate { get; set; }
        public DateTime? ReviewCloseDate { get; set; }
    }
}



using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
    {
        /// <summary>
        /// Lightweight VM for the Step 3 Landing Page.
        /// Shown to the Data Team when they click the email link.
        /// Read-only display of Step 1 configuration before they start processing.
        /// </summary>
        public sealed class WorkflowStep3LandingVm
        {
            public int SettingsId { get; set; }
            public Guid ApprovalKey { get; set; }

            /// <summary>The Step3Kickoff URL to redirect to when "Start Processing" is clicked.</summary>
            public string KickoffUrl { get; set; } = "";
            public string? Variant { get; set; }

            // Roll + notice
            public string RollShortCode { get; set; } = "";
            public string RollName { get; set; } = "";
            public NoticeKind Notice { get; set; }
            public BatchMode Mode { get; set; }
            public int Version { get; set; }

            // Approval metadata
            public string? ApprovedBy { get; set; }
            public DateTime? ApprovedAtUtc { get; set; }

            // Common
            public DateTime LetterDate { get; set; }
            public string? FinancialYearsText { get; set; }
            public string? PortalUrl { get; set; }
            public string? EnquiriesLine { get; set; }
            public DateTime? CityManagerSignDate { get; set; }

            // S49
            public DateTime? ObjectionStartDate { get; set; }
            public DateTime? ObjectionEndDate { get; set; }
            public DateTime? ExtensionDate { get; set; }
            public string? SignaturePath { get; set; }

            // S51
            public DateTime? EvidenceCloseDate { get; set; }

            // S52
            public DateTime? BulkFromDate { get; set; }
            public DateTime? BulkToDate { get; set; }
            public S52SendMode S52SendMode { get; set; } = S52SendMode.Both;

            // S53
            public DateTime? BatchDate { get; set; }
            public DateTime? AppealCloseDate { get; set; }
            public string? AppealCloseOverrideReason { get; set; }

            // IN
            public INSendMode INSendMode { get; set; } = INSendMode.Both;
        }
    }


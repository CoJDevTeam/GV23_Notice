using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class WorkflowStep3KickoffVm
    {
        public int SettingsId { get; set; }
        public Guid ApprovalKey { get; set; }

        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public BatchMode Mode { get; set; }
        public int Version { get; set; }

        // Approval metadata
        public bool IsApproved { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }

        // Step 1 snapshot (read-only display)
        public DateTime LetterDate { get; set; }
        public DateTime? ObjectionStartDate { get; set; }
        public DateTime? ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }
        public string? FinancialYearsText { get; set; }
        public string? SignaturePath { get; set; }

        // Notice-specific dates (for batch panel display)
        public DateTime? EvidenceCloseDate { get; set; }       // S51
        public DateTime? BulkFromDate { get; set; }            // S52
        public DateTime? BulkToDate { get; set; }              // S52
        public bool? IsSection52Review { get; set; }           // S52: true=Review, false=Appeal
        public DateTime? S53BatchDate { get; set; }            // S53
        public DateTime? AppealCloseDate { get; set; }         // S53
        public DateTime? ExtractionDate { get; set; }          // S78
        public int? ExtractPeriodDays { get; set; }            // S78
        public DateTime? ReviewOpenDate { get; set; }          // S78
        public DateTime? ReviewCloseDate { get; set; }         // S78
        public bool? IsInvalidOmission { get; set; }           // IN

        // Batch panel — auto-computed server side
        /// <summary>Pre-computed next batch code e.g. S49_GV23_0003</summary>
        public string NextBatchCode { get; set; } = "";
        /// <summary>Total pending records available for batching</summary>
        public int TotalPendingRecords { get; set; }
        /// <summary>How many STEP3 batches already created for this workflow</summary>
        public int BatchesAlreadyCreated { get; set; }

        // Preview fields
        public string RecipientName { get; set; } = "";
        public string RecipientEmail { get; set; } = "";
        public string EmailSubject { get; set; } = "";
        public string EmailBodyHtml { get; set; } = "";
        public string PdfUrl { get; set; } = "";

        public string SelectedVariant { get; set; } = "Default";
        public string SelectedMode { get; set; } = "single";
        public string? AppealNo { get; set; }

        // S52 range-print specific
        /// <summary>True when the kickoff is for a Section 52 notice (no batch creation UI).</summary>
        public bool IsS52 { get; set; }
        /// <summary>True = Review sub-type, False = Appeal Decision sub-type.</summary>
        public bool S52IsReview { get; set; }
        /// <summary>Total records in the configured date range for the selected sub-type.</summary>
        public int S52RangeCount { get; set; }

        // Real DB preview sample
        public string? SamplePremiseId { get; set; }
        public string? SampleEmail { get; set; }
        public int SampleRowCount { get; set; }
        public bool SampleIsSplit { get; set; }

        public string PdfPreviewUrl { get; set; } = "";
        public string EmailPreviewHtml { get; set; } = "";

        // Batch dashboard
        /// <summary>All batches already created for this workflow, newest first.</summary>
        public List<KickoffBatchRowVm> CreatedBatches { get; set; } = new();

        /// <summary>When true the view auto-switches to the Batch Dashboard tab on load.</summary>
        public bool ShowBatchTab { get; set; }

        /// <summary>
        /// True when the user arrived directly from Step 2 Approve.
        /// Triggers the confirmation modal to auto-open on page load.
        /// </summary>
        public bool FromStep2 { get; set; }
    }

    public sealed class KickoffBatchRowVm
    {
        public int BatchId { get; set; }
        public string BatchName { get; set; } = "";
        public DateTime BatchDate { get; set; }
        public int NumberOfRecords { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public bool IsApproved { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
    }
}
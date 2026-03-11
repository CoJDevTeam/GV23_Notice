using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    /// <summary>Print dashboard view model (Step 3 → Print).</summary>
    public sealed class Step3PrintVm
    {
        public Guid WorkflowKey { get; set; }
        public int SettingsId { get; set; }
        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";
        public NoticeKind Notice { get; set; }
        public string VersionText { get; set; } = "";

        // Workflow summary (readonly)
        public DateTime? LetterDate { get; set; }
        public string? FinancialYearsText { get; set; }
        public DateTime? ObjectionStartDate { get; set; }
        public DateTime? ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }
        public string? SignaturePath { get; set; }

        // Batch stats
        public int TotalBatches { get; set; }
        public int TotalRecordsBatched { get; set; }
        public int TotalPrinted { get; set; }
        public int TotalFailed { get; set; }

        // S52 range-print awareness
        public bool IsS52 { get; set; }
        public bool S52IsReview { get; set; }
        public DateTime? BulkFromDate { get; set; }
        public DateTime? BulkToDate { get; set; }

        public List<Step3PrintBatchRowVm> Batches { get; set; } = new();
    }

    public sealed class Step3PrintBatchRowVm
    {
        public int BatchId { get; set; }
        public string BatchName { get; set; } = "";
        public DateTime BatchDate { get; set; }
        public int NumberOfRecords { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public bool IsApproved { get; set; }

        // Print status counts from NoticeRunLog
        public int PrintedCount { get; set; }
        public int FailedCount { get; set; }
        public int GeneratedCount { get; set; }   // still waiting to print
        public int SentCount { get; set; }

        public bool IsFullyPrinted => GeneratedCount == 0 && PrintedCount > 0 && FailedCount == 0;
        public bool HasAnyPrinted => PrintedCount > 0;
        public bool HasFailed => FailedCount > 0;
        public int RemainingCount => GeneratedCount;
    }
}
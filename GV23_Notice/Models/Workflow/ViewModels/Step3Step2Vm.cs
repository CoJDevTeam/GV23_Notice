using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class Step3Step2Vm
    {
        public Guid WorkflowKey { get; set; }
        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";
        public NoticeKind Notice { get; set; }
        public string VersionText { get; set; } = "";

        public DateTime BatchDate { get; set; } = DateTime.Today;

        public int BatchSize { get; set; } = 500;
        public int TotalPendingRecords { get; set; }
        public int BatchesCreatedCount { get; set; }
        public int TotalRecordsInCreatedBatches { get; set; }

        // readonly summary fields
        public DateTime? LetterDate { get; set; }
        public string? FinancialYearsText { get; set; }
        public DateTime? ObjectionStartDate { get; set; }
        public DateTime? ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }
        public string? SignaturePath { get; set; }

        public List<Step3BatchRowVm> Batches { get; set; } = new();
    }

    public sealed class Step3BatchRowVm
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

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

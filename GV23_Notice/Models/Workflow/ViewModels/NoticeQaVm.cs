

using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class NoticeQaVm
    {
        public Guid WorkflowKey { get; set; }

        public int SettingsId { get; set; }
        public int RollId { get; set; }

        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public string VersionText { get; set; } = "";

        public int? QaRunId { get; set; }

        public string QaStatus { get; set; } = "NotStarted";

        public bool CanApprove { get; set; }
        public bool IsApproved { get; set; }

        public int TotalPrinted { get; set; }
        public int TotalQaItems { get; set; }
        public int FailedItems { get; set; }

        public List<NoticeQaGroupVm> Groups { get; set; } = new();
    }

    public sealed class NoticeQaGroupVm
    {
        public string PropertyType { get; set; } = "";
        public List<NoticeQaItemVm> Items { get; set; } = new();
    }

    public sealed class NoticeQaItemVm
    {
        public int QaItemId { get; set; }
        public int NoticeRunLogId { get; set; }

        public string? ObjectionNo { get; set; }
        public string? PremiseId { get; set; }
        public string? PropertyType { get; set; }
        public string? PropertyDesc { get; set; }

        public string? PdfPath { get; set; }

        public string? NewCategoryMvd { get; set; }
        public string? New2CategoryMvd { get; set; }
        public string? New3CategoryMvd { get; set; }

        public string? ExpectedCategory { get; set; }

        public bool IsCategoryValid { get; set; }

        public string QaStatus { get; set; } = "Pending";
        public string? QaComment { get; set; }

        public bool IsMulti =>
            string.Equals(PropertyType, "Multi", StringComparison.OrdinalIgnoreCase);
    }
}
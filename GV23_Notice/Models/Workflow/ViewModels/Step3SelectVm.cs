using GV23_Notice.Domain.Workflow;
using GV23_Notice.Services.Preview;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class Step3SelectVm
    {
        public int? RollId { get; set; }
        public NoticeKind? Notice { get; set; }

        // dropdowns
        public List<SelectListItem> Rolls { get; set; } = new();
        public List<SelectListItem> Notices { get; set; } = new();

        // results
        public List<Step3WorkflowRowVm> Rows { get; set; } = new();
    }


    public sealed class Step3WorkflowRowVm
    {
        public Guid WorkflowKey { get; set; }
        public int SettingsId { get; set; }

        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public PreviewMode Mode { get; set; }
        public string VersionText { get; set; } = "";

        public DateTime LetterDate { get; set; }

        public bool Step1Approved { get; set; }
        public string Step1ApprovedBy { get; set; } = "-";
        public DateTime? Step1ApprovedAtUtc { get; set; }

        public bool Step2Approved { get; set; }
        public bool Step2CorrectionRequested { get; set; }

        public string StatusLabel
            => Step2Approved ? "APPROVED"
             : Step2CorrectionRequested ? "CORRECTION"
             : "PENDING";
        public Guid ApprovalKey { get;set; }


    }
}

using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class Step2EmailSuccessVm
    {
        public int SettingsId { get; set; }
        /// <summary>"approval" or "correction"</summary>
        public string Kind { get; set; } = "approval";

        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";
        public NoticeKind Notice { get; set; }
        public int Version { get; set; }

        public string RecipientList { get; set; } = "";
        public string EmailSubject { get; set; } = "";
        public string ApprovedBy { get; set; } = "";
        public DateTime? ApprovedAtUtc { get; set; }

        public bool IsApproval => Kind == "approval";
        public bool IsCorrection => Kind == "correction";
    }
}
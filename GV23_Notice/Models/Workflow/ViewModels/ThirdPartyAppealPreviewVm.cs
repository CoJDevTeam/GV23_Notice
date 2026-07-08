namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class ThirdPartyAppealPreviewVm
    {
        public int? NoticeSettingsId { get; set; }
        public int RollId { get; set; }

        public string RollShortCode { get; set; } = "";
        public string ValuationPeriod { get; set; } = "";
        public string Notice { get; set; } = "Third-Party Appeal Application";

        public DateTime LetterDate { get; set; }
        public DateTime ResponseDueDate { get; set; }

        public int TotalRecords { get; set; }
        public int SingleCount { get; set; }
        public int MultipurposeCount { get; set; }

        public ThirdPartyAppealPreviewItemVm? SingleSample { get; set; }
        public ThirdPartyAppealPreviewItemVm? MultipurposeSample { get; set; }

        public bool IsApproved { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }
}

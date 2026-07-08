namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class ThirdPartyAppealDateConfigurationSummaryVm
    {
        public int? RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string ValuationPeriod { get; set; } = "";
        public string Notice { get; set; } = "Third-Party Appeal Application";

        public DateTime EstimatedSendDate { get; set; }
        public int ResponseDays { get; set; } = 51;
        public DateTime EstimatedResponseDueDate { get; set; }

        public int TotalRecords { get; set; }
        public int TotalWithOwnerEmail { get; set; }
        public int TotalMissingOwnerEmail { get; set; }

        public int SingleCount { get; set; }
        public int MultipurposeCount { get; set; }

        public int TotalPrinted { get; set; }
        public int TotalSent { get; set; }
        public int TotalFailed { get; set; }
    }
}

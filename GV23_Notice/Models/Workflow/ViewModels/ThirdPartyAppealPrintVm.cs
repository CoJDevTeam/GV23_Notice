namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class ThirdPartyAppealPrintVm
    {
        public int? NoticeSettingsId { get; set; }
        public int RollId { get; set; }

        public string RollShortCode { get; set; } = "";
        public string ValuationPeriod { get; set; } = "";
        public string Notice { get; set; } = "Third-Party Appeal Application";

        public int TotalRecords { get; set; }
        public int TotalPendingPrint { get; set; }
        public int TotalPrinted { get; set; }
        public int TotalPrintFailed { get; set; }

        public bool PreviewApproved { get; set; }

        public List<ThirdPartyAppealPrintItemVm> Items { get; set; } = new();
    }

    public class ThirdPartyAppealPrintItemVm
    {
        public int Id { get; set; }
        public string AppealNo { get; set; } = "";
        public string ObjectionNo { get; set; } = "";
        public string PropertyDescription { get; set; } = "";
        public string PropertyType { get; set; } = "";
        public string OwnerEmail { get; set; } = "";
        public string Status { get; set; } = "";
        public string PdfPath { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }
}

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
        public Guid WorkflowKey { get; set; }
         public int SettingsId { get; set; }
        public int TotalMissingOwnerEmail { get; set; }
        public List<ThirdPartyAppealPrintGroupVm> Groups { get; set; } = new();
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
   

    public sealed class ThirdPartyAppealPrintGroupVm
    {
        public string GroupName { get; set; } = "";
        public int Total { get; set; }
        public int PendingPrint { get; set; }
        public int Printed { get; set; }
        public int PrintFailed { get; set; }
        public int MissingOwnerEmail { get; set; }
    }
}

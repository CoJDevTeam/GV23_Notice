namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class ThirdPartyAppealPrintResultVm
    {
        public int Total { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public class ThirdPartyAppealPrintProgressVm
    {
        public int Total { get; set; }
        public int Pending { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }

        public bool Done => Total > 0 && Pending == 0;
    }

    public class ThirdPartyAppealEmailResultVm
    {
        public int Total { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    
}

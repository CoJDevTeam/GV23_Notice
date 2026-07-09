namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class ThirdPartyAppealEmailProgressVm
    {
        public int Total { get; set; }
        public int Pending { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int NoOwnerEmail { get; set; }

        public int Skipped { get; set; }

        public bool Done => Total > 0 && Pending == 0;

       
    }
}

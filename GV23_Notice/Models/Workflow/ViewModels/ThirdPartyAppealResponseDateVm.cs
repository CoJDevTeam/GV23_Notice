namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class ThirdPartyAppealResponseDateVm
    {
        public DateTime StartDate { get; set; }
        public int ResponseDays { get; set; } = 51;
        public DateTime ResponseDueDate { get; set; }
    }
}

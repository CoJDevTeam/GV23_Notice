namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class ThirdPartyAppealBoardGroupVm
    {
        public int? VabBoardId { get; set; }

        public string BoardCode { get; set; } = "";

        public string BoardName { get; set; } = "";

        public DateTime? HearingDate { get; set; }

        public List<ThirdPartyAppealBoardMemberVm> Members { get; set; }
            = new();
    }
}

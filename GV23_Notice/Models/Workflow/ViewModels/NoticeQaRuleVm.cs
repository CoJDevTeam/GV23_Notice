namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class NoticeQaRuleVm
    {
        public int TargetTotal { get; set; } = 10;
        public int MaxPerGroup { get; set; } = 3;
        public string GroupLabel { get; set; } = "QA Group";
        public string Description { get; set; } = "";
    }
}

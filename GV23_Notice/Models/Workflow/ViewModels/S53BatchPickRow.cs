namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class S53BatchPickRow
    {
        public string? ObjectionNo { get; set; }
        public string? PremiseId { get; set; }
        public string? RecipientEmail { get; set; }
        public string? ObjectorType { get; set; }  // Owner | Representative | Owner_Rep | Third_Party | Owner_Third_Party
    }
}

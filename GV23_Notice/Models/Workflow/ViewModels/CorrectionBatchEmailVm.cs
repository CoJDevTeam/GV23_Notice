namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class CorrectionBatchEmailVm
    {
        public int BatchId { get; set; }

        public string EmailSubject { get; set; } = "";
        public string EmailBody { get; set; } = "";
        public string EmailCc { get; set; } = "";

        public bool SaveAsTemplate { get; set; } = true;
        public string TemplateName { get; set; } = "Correction Email Template";
    }
}

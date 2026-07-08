using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class CorrectionEmailComposeVm
    {
        public int BatchId { get; set; }

        [Required]
        public string Subject { get; set; } = "";

        [Required]
        public string Body { get; set; } = "";

        public string Cc { get; set; } = "ValuationEnquiries@joburg.org.za";

        public List<CorrectionEmailRecipientVm> Recipients { get; set; } = new();
    }

    public class CorrectionEmailRecipientVm
    {
        public int ItemId { get; set; }
        public string ReferenceNo { get; set; } = "";
        public string PropertyDesc { get; set; } = "";
        public string RecipientRole { get; set; } = "";
        public string RecipientEmail { get; set; } = "";
        public string PdfPath { get; set; } = "";
    }
}

using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class ThirdPartyAppealDateConfigVm
    {
        public int? NoticeSettingsId { get; set; }

        [Required]
        public int? RollId { get; set; }

        public string RollShortCode { get; set; } = "";

        public string Notice { get; set; } = "Third-Party Appeal Application";

        [Required]
        public string ValuationPeriod { get; set; } = "";

        public DateTime EstimatedSendDate { get; set; } = DateTime.Today;

        public int ResponseDays { get; set; } = 51;

        public DateTime EstimatedResponseDueDate { get; set; } = DateTime.Today.AddDays(51);

        public int TotalRecords { get; set; }
        public int TotalWithOwnerEmail { get; set; }
        public int TotalMissingOwnerEmail { get; set; }

        public int SingleCount { get; set; }
        public int MultipurposeCount { get; set; }

        public int TotalPrinted { get; set; }
        public int TotalSent { get; set; }
        public int TotalFailed { get; set; }

        public List<SelectListItem> Rolls { get; set; } = new();
        public List<SelectListItem> ValuationPeriods { get; set; } = new();
    }
}

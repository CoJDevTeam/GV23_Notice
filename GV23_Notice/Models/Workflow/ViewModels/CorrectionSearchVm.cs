using GV23_Notice.Domain.Workflow;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class CorrectionSearchVm
    {
        public int? RollId { get; set; }
        public NoticeKind? Notice { get; set; }

        // Where the corrected data must be pulled from.
        public NoticeKind? SourceNotice { get; set; }

        // Which notice heading/template must be used when printing.
        public NoticeKind? PrintNotice { get; set; }

        public string ReferenceType { get; set; } = "";
        public string ReferenceNo { get; set; } = "";
        public List<SelectListItem> SourceNoticeTypes { get; set; } = new();
        public List<SelectListItem> PrintNoticeTypes { get; set; } = new();
        public List<SelectListItem> Rolls { get; set; } = new();
        public List<SelectListItem> NoticeTypes { get; set; } = new();
        public List<SelectListItem> ReferenceTypes { get; set; } = new();
    }
}

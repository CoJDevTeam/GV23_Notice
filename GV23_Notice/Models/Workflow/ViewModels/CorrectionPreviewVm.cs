using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.Corrections.ViewModels;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class CorrectionPreviewVm
    {
        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";
        public string SourceDb { get; set; } = "";

        public NoticeKind SourceNotice { get; set; }
        public NoticeKind PrintNotice { get; set; }

        public string SourceNoticeText { get; set; } = "";
        public string PrintNoticeText { get; set; } = "";

        // Keep this for compatibility with existing code.
        public NoticeKind Notice
        {
            get => SourceNotice;
            set => SourceNotice = value;
        }

        public string NoticeText
        {
            get => SourceNoticeText;
            set => SourceNoticeText = value;
        }

        public string ReferenceType { get; set; } = "";
        public string ReferenceNo { get; set; } = "";

        public string? NoticeSubKind { get; set; }

        public string CorrectionReason { get; set; } = "";

        public List<CorrectionPreviewItemVm> Items { get; set; } = new();

        public List<CorrectionFieldRowVm> Fields =>
            Items.FirstOrDefault()?.Fields ?? new();
    }
}

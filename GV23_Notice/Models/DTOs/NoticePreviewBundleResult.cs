using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.DTOs
{
    public sealed class NoticePreviewBundleResult
    {
        public int SettingsId { get; set; }
        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";
        public NoticeKind Notice { get; set; }
        public BatchMode Mode { get; set; }
        public int Version { get; set; }

        public string RecipientName { get; set; } = "";
        public string RecipientEmail { get; set; } = "";
        public string AddressLine { get; set; } = "";
        public string SampleObjectionNo { get; set; } = "";
        public string SampleAppealNo { get; set; } = "";

        public List<NoticePreviewTab> Tabs { get; set; } = new();
    }
}

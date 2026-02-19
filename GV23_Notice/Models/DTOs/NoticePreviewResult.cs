using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.DTOs
{
    public sealed class NoticePreviewResult
    {
        public int SettingsId { get; init; }
        public int RollId { get; init; }
        public string RollShortCode { get; init; } = "";
        public string RollName { get; init; } = "";
        public NoticeKind Notice { get; init; }
        public BatchMode Mode { get; init; }
        public int Version { get; init; }

        public string RecipientName { get; init; } = "Notice Sample";
        public string RecipientEmail { get; init; } = "NoticeSample@joburg.org.za";
        public string AddressLine { get; init; } = "66 Jorissen Place, Jorissen Street, Braamfontein";

        public string SampleObjectionNo { get; init; } = "";
        public string SampleAppealNo { get; init; } = "";

        public string EmailSubject { get; init; } = "";
        public string EmailBodyHtml { get; init; } = "";

        public byte[] PdfBytes { get; init; } = Array.Empty<byte>();
        public string PdfFileName { get; init; } = "Preview.pdf";

        public long SnapshotId { get; set; }  // ✅ NEW

       

      
    }
}

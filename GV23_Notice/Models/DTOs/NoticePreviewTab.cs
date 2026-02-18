using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.DTOs
{
    public sealed class NoticePreviewTab
    {
        public string Key { get; set; } = "";          // unique id used by UI (e.g. "single", "multi", "appeal-single")
        public string Title { get; set; } = "";        // tab label
        public NoticeKind Notice { get; set; }         // which notice family
        public bool IsMulti { get; set; }              // single vs multi property pdf
        public string Variant { get; set; } = "";      // e.g. "InvalidObjection", "InvalidOmission", "Appeal", "Review"

        public string EmailSubject { get; set; } = "";
        public string EmailBodyHtml { get; set; } = "";

        public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
        public string PdfFileName { get; set; } = "PREVIEW.pdf";
    }
}

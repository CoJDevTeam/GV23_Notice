namespace GV23_Notice.Services.Notices.Section52
{
    public interface ISection52PdfService
    {
        /// <summary>Final PDF for real sending (DB row)</summary>
        byte[] BuildNotice(AppealDecisionRow row, Section52PdfContext ctx);

        /// <summary>Preview PDF (dummy/sample) for Step2</summary>
        byte[] BuildPreview(Section52PreviewData preview);

        // Optional: keep your legacy entry point if other code already calls it
        byte[] BuildDecisionPdf(AppealDecisionRow model, DateOnly letterDate);
    }
}

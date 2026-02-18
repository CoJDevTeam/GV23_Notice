namespace GV23_Notice.Services.Notices.DearJohnny
{
    public interface IDearJonnyPdfService
    {
        /// <summary>
        /// Final PDF for real sending (DB data)
        /// </summary>
        byte[] BuildNotice(DearJonnyPdfData data, DearJonnyPdfContext ctx);

        /// <summary>
        /// Preview PDF (dummy/sample) for Step2
        /// </summary>
        byte[] BuildPreview(DearJonnyPreviewData preview);
    }
}

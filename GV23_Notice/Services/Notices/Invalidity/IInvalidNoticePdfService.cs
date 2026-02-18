namespace GV23_Notice.Services.Notices.Invalidity
{
    public interface IInvalidNoticePdfService
    {
        /// <summary>
        /// Final PDF for real sending (DB data)
        /// </summary>
        byte[] BuildNotice(InvalidNoticePdfData data, InvalidNoticePdfContext ctx);

        /// <summary>
        /// Preview PDF (dummy/sample) for Step2
        /// </summary>
        byte[] BuildPreview(InvalidNoticePreviewData preview);
    }
}

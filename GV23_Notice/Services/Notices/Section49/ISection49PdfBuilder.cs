using GV23_Notice.Services.Notices.Section78;

namespace GV23_Notice.Services.Notices.Section49
{
    public interface ISection49PdfBuilder
    {
        /// <summary>
        /// Builds the final Section 49 PDF for sending to clients (real DB data).
        /// </summary>
        byte[] BuildNotice(NoticeAttributesModel data, Section49NoticeContext ctx);

        /// <summary>
        /// Builds a preview PDF (dummy/sample data) for Step2 approval.
        /// NOTE: Preview MUST reuse the same template/body as BuildNotice.
        /// </summary>
        byte[] BuildPreview(Section49PreviewData data);
    }
}

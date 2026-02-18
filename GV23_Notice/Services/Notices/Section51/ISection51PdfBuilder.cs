using GV23_Notice.Services.Notices.Section49;

namespace GV23_Notice.Services.Notices.Section51
{
    public interface ISection51PdfBuilder
    {
        /// <summary>Final PDF (real DB data)</summary>
        byte[] BuildNotice(Section51NoticeData data, Section51NoticeContext ctx);

        /// <summary>Step2 template preview (dummy data + approved Step1 dates)</summary>
        byte[] BuildPreview(Section51PreviewData preview);
    }
}

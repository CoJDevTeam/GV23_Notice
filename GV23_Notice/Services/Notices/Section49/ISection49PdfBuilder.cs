using GV23_Notice.Services.Notices.Section52;
using GV23_Notice.Services.Notices.Section78;

namespace GV23_Notice.Services.Notices.Section49
{
    public interface ISection49PdfBuilder
    {
        byte[] BuildNotice(Section49PdfData data, Section49NoticeContext ctx);
    }
}

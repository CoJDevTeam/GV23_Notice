namespace GV23_Notice.Services.Notices.Section78
{
    public interface ISection78PdfBuilder
    {
        byte[] BuildPreview(Section78PreviewData data, Section78PdfContext ctx);
    }
}

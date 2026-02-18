namespace GV23_Notice.Services.Preview
{
    public enum PreviewVariant
    {
        Default = 0,

        // Invalid Notice
        InvalidOmission = 10,
        InvalidObjection = 11,

        // Section 52
        S52AppealDecision = 20,
        S52ReviewDecision = 21
    }

    public enum PreviewMode
    {
        Single = 0,
        EmailMulti = 1,
        SplitPdf = 2
    }
    public sealed class NoticePreviewSelector
    {
        public PreviewVariant Variant { get; set; } = PreviewVariant.Default;

        // Multi property layout (Step2 preview only)
        public bool Multi { get; set; } = false;
    }
}

namespace GV23_Notice.Domain.Section49
{
    public sealed class Section49Options
    {
        public const string SectionName = "Section49";

        public Section49BatchOptions Batch { get; set; } = new();

        public Section49AuditOptions Audit { get; set; } = new();

        public Section49StorageDefaultsOptions StorageDefaults { get; set; } = new();

        public Section49QaOptions Qa { get; set; } = new();
    }

    public sealed class Section49BatchOptions
    {
        public int Size { get; set; } = 500;

        public bool RequireFullBatch { get; set; } = true;
    }

    public sealed class Section49AuditOptions
    {
        public bool Enabled { get; set; } = true;
    }

    public sealed class Section49StorageDefaultsOptions
    {
        public string PdfFileNamePattern { get; set; } =
            "Section49_{PropertyDesc}.pdf";

        public string EmlFileNamePattern { get; set; } =
            "email_{PropertyDesc}_{OriginalEmail}.eml";

        public bool CreatePropertyFolderForPdf { get; set; } = true;

        public bool CreatePropertyFolderForEmail { get; set; } = false;
    }

    public sealed class Section49QaOptions
    {
        public bool Enabled { get; set; } = true;

        public bool RequireApprovalBeforeSend { get; set; } = true;

        public bool RequireAllNoticesPrintedBeforeQa { get; set; } = true;

        public List<Section49QaSampleOptions> Samples { get; set; } = new();
    }

    public sealed class Section49QaSampleOptions
    {
        public string Key { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int Count { get; set; } = 1;
    }
}
namespace GV23_Notice.Domain.Section49Sup4
{
    public sealed class Sup4Section49Options
    {
        public bool TestMode { get; set; }

        public string TestRecipient { get; set; } =
            "JabulaniSib@joburg.org.za";

        public int BatchSize { get; set; } = 500;

        public string SignaturePath { get; set; } =
            @"C:\Sup4\Sup 4 Signature";

        public string PdfRootPath { get; set; } =
            @"C:\Sup4\Sup 4 Notices\Section 49 Notices";

        public string EmailRootPath { get; set; } =
            @"C:\Sup4\Sup 4 Emails\Section 49 Notice Emails";

        public string? ProductionSignaturePath { get; set; }

        public string? ProductionPdfRootPath { get; set; }

        public string? ProductionEmailRootPath { get; set; }
    }
}
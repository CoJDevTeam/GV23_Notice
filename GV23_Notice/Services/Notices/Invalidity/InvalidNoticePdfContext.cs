namespace GV23_Notice.Services.Notices.Invalidity
{
    public sealed class InvalidNoticePdfContext
    {
        public string HeaderImagePath { get; set; } = "";
        public DateTime LetterDate { get; set; } = DateTime.Today;

        public string EnquiriesLine { get; set; } =
            "For any enquiries, please contact us on 011 407 6622 or 011 407 6597, or email us at valuationenquiries@joburg.org.za";
    }

    public sealed class InvalidNoticePdfData
    {
        public InvalidNoticeKind Kind { get; set; } = InvalidNoticeKind.InvalidObjection;

        public string ObjectionNo { get; set; } = "";
        public string PropertyDescription { get; set; } = "";

        // This is the source address block (multi-line)
        public string? RecipientAddress { get; set; }

        public string? RecipientName { get; set; }

        // Optional explicit lines (if you also support them elsewhere)
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }
    }

    /// <summary>
    /// Dummy/sample values for Step2 preview
    /// </summary>
    public sealed class InvalidNoticePreviewData
    {
        public string HeaderImagePath { get; set; } = "";
        public DateTime LetterDate { get; set; } = DateTime.Today;

        public string EnquiriesLine { get; set; } =
            "For any enquiries, please contact us on 011 407 6622 or 011 407 6597, or email us at valuationenquiries@joburg.org.za";

        public InvalidNoticeKind Kind { get; set; } = InvalidNoticeKind.InvalidObjection;

        public string ObjectionNo { get; set; } = "OBJ-GV23-1";
        public string PropertyDescription { get; set; } = "PORTION 2 ERF 201 ROSEBANK";

        public string RecipientName { get; set; } = "Sir/Madam";

        // Use multiline address like your current builder expects
        public string RecipientAddress { get; set; } =
            "XXXX\nXXXX\nXXXX\nXXXX";
    }
}

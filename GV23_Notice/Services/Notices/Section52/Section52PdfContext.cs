namespace GV23_Notice.Services.Notices.Section52
{
    public sealed class Section52PdfContext
    {
        public string HeaderImagePath { get; set; } = "";
        public DateOnly LetterDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    }
    public sealed class Section52PreviewData
    {
        public string HeaderImagePath { get; set; } = "";
        public DateOnly LetterDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        // Review vs Appeal
        public bool IsReview { get; set; } = false;

        // Recipient
        public string RecipientName { get; set; } = "SIR/MADAM";
        public string Email { get; set; } = "dummy@example.com";

        // Property
        public string PropertyDescription { get; set; } = "PORTION 2 ERF 201 ROSEBANK";
        public string Town { get; set; } = "ROSEBANK";
        public string Erf { get; set; } = "201";
        public string Portion { get; set; } = "2";
        public string Re { get; set; } = "";

        // Refs
        public string ObjectionNo { get; set; } = "OBJ-GV23-0001";
        public string AppealNo { get; set; } = "APL-GV23-0001";

        // Decision values
        public string Category { get; set; } = "Residential";
        public object? Extent { get; set; } = "1000";
        public object? Extent2 { get; set; } = "";
        public object? Extent3 { get; set; } = "";

        public object? MarketValue { get; set; } = "3000000";
        public object? MarketValue2 { get; set; } = "";
        public object? MarketValue3 { get; set; } = "";

        public string ValuationKey { get; set; } = "KEY-XXXX";
    }
}
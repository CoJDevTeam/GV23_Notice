namespace GV23_Notice.Services.Notices.Section49
{
    public sealed class Section49PreviewData
    {
        // Context inputs (from approved Step1)
        public DateTime LetterDate { get; set; } = DateTime.Today;
        public DateTime InspectionStartDate { get; set; } = DateTime.Today;
        public DateTime InspectionEndDate { get; set; } = DateTime.Today.AddDays(30);
        public DateTime? ExtendedEndDate { get; set; }

        public string? FinancialYearsText { get; set; } 
        public string? RollHeaderText { get; set; } 

        public string HeaderImagePath { get; set; } = "";
        public string? SignaturePath { get; set; }

        // ✅ NEW: allow Step2 SplitPdf to render 4 rows
        public bool ForceFourRows { get; set; } = false;

        // ✅ NEW: rows for the property table (max 4 used)
        public List<Section49PropertyRow>? PropertyRows { get; set; }
    }
    public sealed class Section49PropertyRow
    {
        public string MarketValue { get; set; } = "";
        public string Extent { get; set; } = "";
        public string Category { get; set; } = "";
        public string Remarks { get; set; } = "";
    }
}

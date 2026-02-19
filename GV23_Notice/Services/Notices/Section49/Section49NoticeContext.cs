namespace GV23_Notice.Services.Notices.Section49
{
    public sealed class Section49NoticeContext
    {
        public string HeaderImagePath { get; set; } = "";
        public DateTime LetterDate { get; set; } = DateTime.Today;

        public DateTime InspectionStartDate { get; set; }
        public DateTime InspectionEndDate { get; set; }
        public DateTime? ExtendedEndDate { get; set; }

        public string? FinancialYearsText { get; set; }
        public string? RollHeaderText { get; set; }

        public string? SignaturePath { get; set; }

        public bool ForceFourRows { get; set; } = false;
        public List<Section49PropertyRow>? PropertyRows { get; set; }


    }
}

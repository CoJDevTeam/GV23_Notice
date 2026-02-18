namespace GV23_Notice.Services.Notices.Section51
{
    public sealed class Section51PreviewData
    {
        // From approved Step1
        public DateTime LetterDate { get; set; }
        public DateTime SubmissionsCloseDate { get; set; } // LetterDate + 30 (auto)
        public string PortalUrl { get; set; } = "https://objections.joburg.org.za/";
        public string HeaderImagePath { get; set; } = "";

        // Optional sign-off overrides (use app defaults if empty)
        public string? SignOffName { get; set; }
        public string? SignOffTitle { get; set; }

        // For label text / roll naming in preview
        public string RollName { get; set; } = "General Valuation Roll 2023";

        // Dummy refs
        public string ObjectionNo { get; set; } = "OBJ-GV23-1";
        public string Section51Pin { get; set; } = "123456";
        public string PropertyFrom { get; set; } = "Omission"; // or "Printed"
    }
}

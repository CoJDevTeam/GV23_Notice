namespace GV23_Notice.Models.DTOs.Section49Sup4
{
    public sealed class Sup4Section49QaResult
    {
        public string BatchName { get; set; } = string.Empty;

        public Sup4Section49QaSampleDto? SectionalTitle { get; set; }

        public Sup4Section49QaSampleDto? MultiPurpose { get; set; }

        public Sup4Section49QaSampleDto? SingleProperty { get; set; }

        public bool IsApproved { get; set; }

        public string? ApprovedBy { get; set; }

        public DateTime? ApprovedDate { get; set; }

        public string? Comment { get; set; }
    }
}
namespace GV23_Notice.Models.DTOs.Section49Sup4
{
    public sealed class Sup4Section49BatchCreateResult
    {
        public string BatchName { get; set; } = string.Empty;

        public DateTime BatchDate { get; set; }

        public int TotalRecords { get; set; }

        public int WithEmail { get; set; }

        public int WithoutEmail { get; set; }

        public int SectionalTitleCount { get; set; }

        public int MultiPurposeCount { get; set; }

        public int SinglePropertyCount { get; set; }

        public bool IsFullBatch => TotalRecords == 500;

        public string? ErrorMessage { get; set; }
    }
}
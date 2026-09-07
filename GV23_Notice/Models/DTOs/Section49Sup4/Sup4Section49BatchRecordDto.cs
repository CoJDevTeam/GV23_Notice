namespace GV23_Notice.Models.DTOs.Section49Sup4
{
    public sealed class Sup4Section49BatchRecordDto
    {
        public long Id { get; set; }

        public string PremiseId { get; set; } = string.Empty;

        public string PropertyDesc { get; set; } = string.Empty;

        public string? EmailAddr { get; set; }

        public string HasEmail { get; set; } = "No";

        public string BatchName { get; set; } = string.Empty;

        public DateTime BatchDate { get; set; }

        public string SendStatus { get; set; } = string.Empty;

        public string? PdfPath { get; set; }

        public string? EmlPath { get; set; }

        public DateTime? SentDate { get; set; }

        public string? SentBy { get; set; }

        public string? ActualSentTo { get; set; }

        public bool IsTestMode { get; set; }
    }
}
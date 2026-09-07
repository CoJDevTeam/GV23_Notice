namespace GV23_Notice.Models.DTOs.Section49Sup4
{
    public sealed class Sup4Section49QaSampleDto
    {
        public long Section49Id { get; set; }

        public string PremiseId { get; set; } = string.Empty;

        public string PropertyDesc { get; set; } = string.Empty;

        public string PropertyType { get; set; } = string.Empty;

        public string? Category { get; set; }

        public string? EmailAddress { get; set; }

        public string? PdfPath { get; set; }

        public bool SelectedForQa { get; set; }

        public bool Approved { get; set; }

        public string? QaComment { get; set; }
    }
}
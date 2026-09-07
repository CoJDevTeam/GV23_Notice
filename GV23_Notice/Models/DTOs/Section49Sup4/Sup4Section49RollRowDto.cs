namespace GV23_Notice.Models.DTOs.Section49Sup4
{
    public sealed class Sup4Section49RollRowDto
    {
        public string PremiseId { get; set; } = string.Empty;

        public string PropertyDesc { get; set; } = string.Empty;

        public string? Sector { get; set; }

        public string? CatDesc { get; set; }

        public string? LisStreetAddress { get; set; }

        public decimal MarketValue { get; set; }

        public decimal RateableArea { get; set; }

        public string? Reason { get; set; }

        public DateTime? WefDate { get; set; }

        public string? ValuationKey { get; set; }

        public string? EmailSent { get; set; }

        public string? BatchName { get; set; }

        public DateTime? BatchDate { get; set; }
    }
}
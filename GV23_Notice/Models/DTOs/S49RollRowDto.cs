namespace GV23_Notice.Models.DTOs
{
    namespace GV23_Notice.Models.DTOs
    {
        public sealed class S49RollRowDto
        {
            public string PremiseId { get; set; } = "";

            public string? PropertyDesc { get; set; }

            public string? LisStreetAddress { get; set; }

            public string? CatDesc { get; set; }

            public decimal MarketValue { get; set; }

            // Keep decimal for calculations / split totals
            public decimal Extent { get; set; }

            // Use this for PDF/display so 10.58 or 10,58 can be preserved
            public string? ExtentText { get; set; }

            public string? Reason { get; set; }

            public int EmailSent { get; set; } // Email_Sent

            public string? ValuationSplitIndicator { get; set; }

            public string? ValuationKey { get; set; }
        }
    }

    public sealed class SapContactDto
    {
        public string PremiseId { get; set; } = "";
        public string? Email { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }
        public string? PremiseAddress { get; set; }
        public string? AccountNo { get; set; }
    }
}

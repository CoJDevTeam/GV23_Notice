namespace GV23_Notice.Services.Notices.Section78
{
    public sealed class Section78PdfContext
    {
        public string HeaderImagePath { get; set; } = "";
        public string EnquiriesLine1 { get; set; } = "Telephone 011-407-6622 or 011-407-6597";
        public string EnquiriesLine2 { get; set; } = "Email valuationenquiries@joburg.org.za";
        public string WebsiteUrl { get; set; } = "https://eservices.joburg.org.za/Pages/Overview.aspx";
    }

    public sealed class Section78PreviewData
    {
        public string OwnerName { get; set; } = "BANK THE";
        public string PostalLine1 { get; set; } = "32 ROSEBANK";
        public string PostalLine2 { get; set; } = "ROSEBANK";
        public string PostalCode { get; set; } = "2196";

        public DateOnly LetterDate { get; set; }

        public DateOnly ReviewOpenDate { get; set; }
        public DateOnly ReviewCloseDate { get; set; }

        public string PropertyDescription { get; set; } = "PORTION 2 ERF 201 ROSEBANK";
        public string PremiseId { get; set; } = "10603379";

        public string PropertyCategory { get; set; } = "Business and Commercial";
        public decimal MarketValue { get; set; } = 349_610_000m;
        public decimal Extent { get; set; } = 2000m;
        public DateOnly EffectiveDate { get; set; } = new DateOnly(2024, 9, 27);

        public string MunicipalValuer { get; set; } = "Municipal Valuer";
    }
}

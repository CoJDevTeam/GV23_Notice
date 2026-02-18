namespace GV23_Notice.Services.Preview
{
    public sealed class DummyPreviewDataFactory : IDummyPreviewDataFactory
    {
        public DummyRecipient GetRecipient()
            => new("Notice Sample", "NoticeSample@joburg.org.za", "66 Jorissen Place, Jorissen Street, Braamfontein");

        public DummyProperty GetProperty()
            => new(
                PropertyDescription: "PORTION 2 ERF 201 ROSEBANK",
                PremiseId: "10603379",
                Category: "Business and Commercial",
                MarketValue: 349_610_000m,
                Extent: 2000m,
                EffectiveDate: new DateOnly(2024, 9, 27)
            );

        public (string objectionNo, string appealNo) GetSampleRefs(string rollShortCode)
        {
            rollShortCode = (rollShortCode ?? "").Trim().ToUpperInvariant();

            return rollShortCode switch
            {
                "GV23" => ("OBJ-GV23-1", "APP-GV23-1"),
                "SUPP 1" => ("GV23-Sup1-1", "GV23-Sup1-1"),
                "SUPP 2" => ("GV23-Sup2-1", "GV23-Sup2-1"),
                "SUPP 3" => ("GV23-Sup3-1", "GV23-Sup3-1"),
                "QUERY" => ("OBJ-QUERY-1", "APP-QUERY-1"),
                _ => ("OBJ-GV23-1", "APP-GV23-1")
            };
        }
    }
}


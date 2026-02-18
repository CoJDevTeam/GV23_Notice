namespace GV23_Notice.Helper
{
    public sealed record ValuationPeriodDef(string Code, DateTime Start, DateTime End);

    public static class ValuationPeriodCatalog
    {
        // Update/extend when needed
        public static readonly List<ValuationPeriodDef> Periods = new()
    {
        new ValuationPeriodDef("GV18", new DateTime(2018, 7, 1), new DateTime(2023, 6, 30)),
        new ValuationPeriodDef("GV23", new DateTime(2023, 7, 1), new DateTime(2027, 6, 30)),
        new ValuationPeriodDef("GV27", new DateTime(2027, 7, 1), new DateTime(2031, 6, 30)),
    };

        public static ValuationPeriodDef? Get(string code)
            => Periods.FirstOrDefault(p => p.Code.Equals(code?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static List<(DateTime start, DateTime end, string text)> BuildFinancialYears(ValuationPeriodDef period)
        {
            // Financial year is always 1 July -> 30 June
            var years = new List<(DateTime start, DateTime end, string text)>();

            var start = new DateTime(period.Start.Year, 7, 1);
            var end = new DateTime(period.Start.Year + 1, 6, 30);

            while (start <= period.End)
            {
                var text = $"{start:dd MMMM yyyy} – {end:dd MMMM yyyy}";
                years.Add((start, end, text));

                start = start.AddYears(1);
                end = end.AddYears(1);
            }

            // Only keep those inside the valuation period boundary
            return years
                .Where(y => y.start >= period.Start && y.end <= period.End)
                .ToList();
        }
    }

}

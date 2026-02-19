namespace GV23_Notice.Services
{
    public sealed class ValuationPeriodDef
    {
        public string Code { get; init; } = ""; // "GV23"
        public DateTime Start { get; init; }    // 2023-07-01
        public DateTime End { get; init; }      // 2027-06-30
    }

    public static class ValuationPeriodCatalog
    {
        private static readonly List<ValuationPeriodDef> _periods = new()
    {
        new() { Code="GV18", Start=new DateTime(2018,7,1), End=new DateTime(2023,6,30) },
        new() { Code="GV23", Start=new DateTime(2023,7,1), End=new DateTime(2027,6,30) },
        // add future when ready:
        // new() { Code="GV27", Start=new DateTime(2027,7,1), End=new DateTime(2031,6,30) },
    };

        public static IReadOnlyList<ValuationPeriodDef> All() => _periods;
        public static ValuationPeriodDef? Get(string code) =>
            _periods.FirstOrDefault(p => p.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

        public static List<(DateTime start, DateTime end, string label)> FinancialYearsFor(string code)
        {
            var p = Get(code);
            if (p is null) return new();

            // Financial year always: 1 July - 30 June
            var years = new List<(DateTime, DateTime, string)>();

            var startYear = p.Start.Year; // 2023
            var endYear = p.End.Year;     // 2027

            // e.g. 2023/2024, 2024/2025, 2025/2026, 2026/2027
            for (int y = startYear; y < endYear; y++)
            {
                var fyStart = new DateTime(y, 7, 1);
                var fyEnd = new DateTime(y + 1, 6, 30);
                years.Add((fyStart, fyEnd, $"{fyStart:dd MMM yyyy} – {fyEnd:dd MMM yyyy}"));
            }

            return years;
        }
    }
}

namespace GV23_Notice.Domain.Rolls
{
    public sealed class RollDbOptions
    {
        public const string SectionName = "RollDb";

        public string BaseSqlConnection { get; set; } = string.Empty;

        /// <summary>
        /// Key = RollRegistry.SourceDb
        ///
        /// Example:
        /// Objection
        /// Objection_Supp1
        /// Objection_Supp2
        /// Objection_Supp3
        /// Objection_Supp4
        /// </summary>
        public Dictionary<string, RollSourceOptions> Sources { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public RollSourceOptions GetSource(string sourceDb)
        {
            if (string.IsNullOrWhiteSpace(sourceDb))
            {
                throw new InvalidOperationException(
                    "Roll source database is empty.");
            }

            if (!Sources.TryGetValue(
                    sourceDb.Trim(),
                    out var source))
            {
                throw new InvalidOperationException(
                    $"No RollDb source configuration exists for '{sourceDb}'.");
            }

            return source;
        }
    }
}
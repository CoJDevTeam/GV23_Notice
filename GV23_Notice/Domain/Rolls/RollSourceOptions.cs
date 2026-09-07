namespace GV23_Notice.Domain.Rolls
{
    public sealed class RollSourceOptions
    {
        /// <summary>
        /// Main valuation roll table.
        ///
        /// Examples:
        /// GV
        /// Sup1
        /// Sup2
        /// Sup3
        /// Sup4
        /// </summary>
        public string RollTable { get; set; } = string.Empty;

        /// <summary>
        /// Contact/postal source table.
        ///
        /// Examples:
        /// sapContacts
        /// Supp4_Postal_address
        /// </summary>
        public string ContactTable { get; set; } = string.Empty;

        public RollHolidayOptions? Holiday { get; set; }

        public RollSection49Options Section49 { get; set; } = new();
    }
}
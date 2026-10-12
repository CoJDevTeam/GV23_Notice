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

        /// <summary>
        /// Optional roll mailbox, e.g. GV23Supp4@joburg.org.za.
        ///
        /// When set, EVERY notice sent to clients on this roll (S49, S51, S52, S53,
        /// DJ, IN, S78, TPA, CLA third party, corrections) is also delivered here:
        ///   - owner has an email  → owner (To) + roll mailbox (Bcc)
        ///   - owner has no email  → roll mailbox only (To)
        /// Blank means no copy. Test mode never replaces this address.
        /// </summary>
        public string RollMailbox { get; set; } = string.Empty;

        public RollHolidayOptions? Holiday { get; set; }

        public RollSection49Options Section49 { get; set; } = new();
    }
}
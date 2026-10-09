namespace GV23_Notice.Domain.Rolls
{
    public sealed class RollSection49Options
    {
        /// <summary>
        /// Controls how the source roll's Email_Sent column is interpreted.
        ///
        /// LegacyWorkflowStatus:
        ///     P / Y / N / NP
        ///
        /// EmailAvailability:
        ///     Yes / No only
        /// </summary>
        public string EmailSentMode { get; set; } =
            RollSection49EmailSentModes.LegacyWorkflowStatus;

        /// <summary>
        /// Optional Section 49 audit table.
        ///
        /// Example:
        /// Section49Table
        ///
        /// Blank means legacy behaviour.
        /// </summary>
        public string AuditTable { get; set; } = string.Empty;

        public RollSection49StorageOptions? Storage { get; set; }

        /// <summary>
        /// Optional roll mailbox, e.g. GV23Supp4@joburg.org.za.
        ///
        /// When set, every Section 49 notice on this roll is also delivered here:
        ///   • owner has an email  → owner (To) + roll mailbox (Bcc)
        ///   • owner has no email  → roll mailbox only (To)
        /// So the mailbox ends up with one email per property on the roll.
        /// Applies in test mode too (the owner copy goes to the test recipient).
        /// </summary>
        public string RollMailbox { get; set; } = string.Empty;

        public bool HasAuditTable =>
            !string.IsNullOrWhiteSpace(AuditTable);
    }

    public static class RollSection49EmailSentModes
    {
        public const string LegacyWorkflowStatus =
            "LegacyWorkflowStatus";

        public const string EmailAvailability =
            "EmailAvailability";
    }
}
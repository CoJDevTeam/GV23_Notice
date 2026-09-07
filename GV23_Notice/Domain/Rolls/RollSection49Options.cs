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
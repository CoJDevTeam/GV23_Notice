using GV23_Notice.Domain.Rolls;

namespace GV23_Notice.Services.Email
{
    /// <summary>Who an email goes to. See <see cref="RollMailbox.Plan"/>.</summary>
    public sealed record RecipientPlan(
        string To,
        string? Bcc,
        bool MailboxOnly,
        string? OwnerEmail);

    /// <summary>
    /// Roll mailbox copy for every notice sent to clients (S49, S51, S52, S53, DJ, IN, S78,
    /// TPA, CLA third party, corrections).
    ///
    /// Config (per roll source):
    ///   RollDb:Sources:{SourceDb}:RollMailbox            e.g. "GV23Supp4@joburg.org.za"
    ///   RollDb:Sources:{SourceDb}:Section49:RollMailbox  (older setting, still read as a fallback)
    ///
    /// Rules:
    ///   owner has an email  → To = owner, Bcc = roll mailbox
    ///   owner has no email  → To = roll mailbox only
    ///   no owner, no mailbox → error (row is marked failed / no email as before)
    /// </summary>
    public static class RollMailbox
    {
        public const string NoOwnerEmailNote =
            "No owner email on record. Notice sent to the roll mailbox only.";

        /// <summary>The mailbox for a bound roll source, or null.</summary>
        public static string? FromSource(RollSourceOptions? source) =>
            Clean(source?.RollMailbox) ??
            Clean(source?.Section49?.RollMailbox);

        /// <summary>The mailbox for a roll source read straight from configuration, or null.</summary>
        public static string? FromConfig(
            IConfiguration config,
            string? sourceDb)
        {
            if (string.IsNullOrWhiteSpace(sourceDb))
                return null;

            var section =
                config.GetSection(
                    $"RollDb:Sources:{sourceDb.Trim()}");

            return Clean(section["RollMailbox"]) ??
                   Clean(section["Section49:RollMailbox"]);
        }

        /// <summary>
        /// Decides who an email goes to. Pure logic — unit tested.
        /// The roll mailbox is internal, so test mode never replaces it.
        /// </summary>
        public static RecipientPlan Plan(
            string? ownerEmail,
            string? rollMailbox,
            bool testMode,
            string? testRecipient,
            string? reference = null)
        {
            var owner = Clean(ownerEmail);
            var mailbox = Clean(rollMailbox);

            if (owner == null)
            {
                if (mailbox == null)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(reference)
                            ? "Recipient email is empty."
                            : $"Recipient email is empty for {reference}.");
                }

                return new RecipientPlan(mailbox, null, true, null);
            }

            var to = owner;

            if (testMode)
            {
                if (string.IsNullOrWhiteSpace(testRecipient))
                {
                    throw new InvalidOperationException(
                        "Email TestMode is enabled but Email:TestMode:Recipient is empty.");
                }

                to = testRecipient.Trim();
            }

            var bcc =
                mailbox != null &&
                !string.Equals(to, mailbox, StringComparison.OrdinalIgnoreCase)
                    ? mailbox
                    : null;

            return new RecipientPlan(to, bcc, false, owner);
        }

        /// <summary>Adds the plan's To and Bcc to a message.</summary>
        public static void Apply(
            System.Net.Mail.MailMessage message,
            RecipientPlan plan)
        {
            message.To.Add(plan.To);

            if (!string.IsNullOrWhiteSpace(plan.Bcc))
                message.Bcc.Add(plan.Bcc);
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
    }
}

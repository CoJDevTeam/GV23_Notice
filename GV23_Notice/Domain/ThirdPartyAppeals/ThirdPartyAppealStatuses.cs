namespace GV23_Notice.Domain.ThirdPartyAppeals
{
    public static class ThirdPartyAppealStatuses
    {
        public const string Pending = "Pending";
        public const string PreviewApproved = "Preview-Approved";

        public const string Printed = "Printed";
        public const string PrintFailed = "Print-Failed";

        public const string ReadyToSend = "Ready-To-Send";
        public const string Sent = "Sent";
        public const string EmailFailed = "Email-Failed";
        public const string NoOwnerEmail = "No-Owner-Email";
        public const string SentWithErrors = "Sent-With-Errors";
    }
}

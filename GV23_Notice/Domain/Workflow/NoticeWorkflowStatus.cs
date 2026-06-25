namespace GV23_Notice.Domain.Workflow
{
    public static class NoticeWorkflowStatus
    {
        public const string PrintingPending = "Printing-Pending";
        public const string QaPending = "QA-Pending";
        public const string EmailSentPending = "Email-Sent-Pending";
        public const string NoticeSent = "Notice-Sent";

        public static readonly string[] CreateBatchEligibleStatuses =
        {
            "Obj-Finalized",
            "Printing-Pending",
            "MVD-Finalized",
            "Obj-Finalized-MVD",
            "Finalized"
        };
    }
}
namespace GV23_Notice.Domain.Workflow
{
    public enum RollCode
    {
        GV23 = 1,
        SUPP1 = 2,
        SUPP2 = 3,
        SUPP3 = 4,
        QUERY = 5
    }

    public enum NoticeKind
    {
        S49 = 1,
        S51 = 2,
        S52 = 3,
        S53 = 4,
        DJ = 5,
        IN = 6,
        S78 = 7// S78 later
    }

    public enum BatchMode
    {
        Single = 1,
        Bulk = 2
    }

    public enum ApprovalAction
    {
        Created = 1,
        Confirmed = 2,
        Approved = 3,
        RequestedCorrection = 4,
        Rejected = 5,
        OverrodeDate = 6
    }

    public enum TicketStatus
    {
        Open = 1,
        InProgress = 2,
        Resolved = 3,
        Cancelled = 4
    }

    public enum RunStatus
    {
        Generated = 1,
        Sent = 2,
        Failed = 3,
        NoEmail = 4,
            Printed = 5
    }
}

namespace GV23_Notice.Domain.Workflow.Entities
{
    public static class NoticeKindExtensions
    {
        public static bool IsSection53Family(this NoticeKind notice)
        {
            return notice == NoticeKind.S53
                || notice == NoticeKind.S53Rev;
        }

        public static bool IsRevisedMvd(this NoticeKind notice)
        {
            return notice == NoticeKind.S53Rev;
        }
    }
}

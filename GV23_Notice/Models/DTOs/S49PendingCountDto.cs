namespace GV23_Notice.Models.DTOs
{
    public sealed class S49PendingCountDto
    {
        public int PendingPremiseCount { get; set; }
        public int PendingRowCount { get; set; }
    }

    public sealed class S49AssignBatchResultDto
    {
        public int PremiseCount { get; set; }
        public int RowCount { get; set; }
    }
}

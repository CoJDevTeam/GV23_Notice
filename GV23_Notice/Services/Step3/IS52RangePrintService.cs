namespace GV23_Notice.Services.Step3
{
    public interface IS52RangePrintService
    {
        /// <summary>Count of Appeal_Decision rows in the configured date range for this settings.</summary>
        Task<int> CountRangeAsync(int settingsId, bool isReview, CancellationToken ct);

        /// <summary>
        /// Creates a tracking batch, generates and saves a PDF for every record in the
        /// date range, and marks each run-log as Printed — all in one atomic call.
        /// </summary>
        Task<S52PrintRangeResult> PrintRangeAsync(int settingsId, bool isReview, string printedBy, CancellationToken ct);
    }

    public sealed class S52PrintRangeResult
    {
        public int Printed { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
        public Guid WorkflowKey { get; set; }
    }
}

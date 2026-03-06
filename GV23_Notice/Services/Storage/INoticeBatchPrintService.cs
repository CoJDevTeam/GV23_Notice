namespace GV23_Notice.Services.Storage
{
    public interface INoticeBatchPrintService
    {
        /// <summary>Print all Generated run logs for a single batch.</summary>
        Task<PrintBatchResult> PrintBatchAsync(int noticeBatchId, string printedBy, CancellationToken ct);

        /// <summary>Print all Generated run logs across every batch in a workflow.</summary>
        Task<PrintAllResult> PrintAllBatchesAsync(Guid workflowKey, string printedBy, CancellationToken ct);
    }

    public sealed class PrintBatchResult
    {
        public int BatchId { get; set; }
        public int Total { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
    }

    public sealed class PrintAllResult
    {
        public int TotalBatches { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
    }
}

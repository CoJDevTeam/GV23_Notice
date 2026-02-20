namespace GV23_Notice.Services.Storage
{
    public interface INoticeBatchPrintService
    {
        Task<PrintBatchResult> PrintBatchAsync(int noticeBatchId, string printedBy, CancellationToken ct);
    }

    public sealed class PrintBatchResult
    {
        public int BatchId { get; set; }
        public int Total { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
    }
}

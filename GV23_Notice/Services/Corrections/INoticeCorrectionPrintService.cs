namespace GV23_Notice.Services.Corrections
{
    public interface INoticeCorrectionPrintService
    {
        Task PrintBatchAsync(int batchId, string printedBy, CancellationToken ct);
    }
}

namespace GV23_Notice.Services.Workflow
{
    public interface INoticeSourceStatusService
    {
        Task<bool> IsS53StatusAsync(
            int rollId,
            string objectionNo,
            string expectedStatus,
            CancellationToken ct);

        Task SetS53StatusAsync(
            int rollId,
            IEnumerable<string> objectionNos,
            string newStatus,
            CancellationToken ct);
    }
}

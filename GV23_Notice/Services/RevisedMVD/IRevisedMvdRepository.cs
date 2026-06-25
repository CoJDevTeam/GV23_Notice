namespace GV23_Notice.Services.RevisedMVD
{
    public interface IRevisedMvdRepository
    {
        Task<List<dynamic>> GetDashboardTotalsAsync(int rollId, CancellationToken ct);

        Task<List<dynamic>> SearchToSendAsync(
            int rollId,
            string? searchString,
            string? searchBy,
            CancellationToken ct);

        Task<List<dynamic>> SearchSentAsync(
            int rollId,
            string? searchString,
            string? searchBy,
            CancellationToken ct);

        Task<List<dynamic>> InsertOwnerRevisedMvdAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct);

        Task<List<dynamic>> InsertSecondRevisedMvdAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct);

        Task MarkMvdSentAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct);

        Task MarkRevisedMvdEmailsSentAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct);

        Task MarkMailDoneRevisedMvdAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct);

        Task PrintReviseMvdAsync(int rollId, CancellationToken ct);

        Task BatchDateReviseMvdAsync(int rollId, CancellationToken ct);

        Task BatchUpdateAsync(int rollId, CancellationToken ct);

        Task ReviseMvdPrintDoneAsync(int rollId, CancellationToken ct);

        Task CancelReviseMvdAsync(int rollId, CancellationToken ct);

        Task GvDataReviseMvdAsync(int rollId, CancellationToken ct);

        Task Section52ReviewReviseMvdAsync(int rollId, CancellationToken ct);

        Task<List<dynamic>> SendEmailReviseMvdAsync(
            int rollId,
            CancellationToken ct);

        Task<List<dynamic>> SaveReviseMvdNoticeAsync(
            int rollId,
            CancellationToken ct);

        Task<List<dynamic>> ReviseMvdPrintAsync(
            int rollId,
            CancellationToken ct);
    }
}

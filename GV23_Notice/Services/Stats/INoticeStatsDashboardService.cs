using GV23_Notice.Models.Stats;

namespace GV23_Notice.Services.Stats
{
    public interface INoticeStatsDashboardService
    {
        Task<NoticeStatsDashboardVm> BuildDashboardAsync(
            NoticeStatsFilterVm filter,
            CancellationToken ct);

        Task<NoticeStatsDetailsVm> BuildDetailsAsync(
            int batchId,
            CancellationToken ct);
    }
}
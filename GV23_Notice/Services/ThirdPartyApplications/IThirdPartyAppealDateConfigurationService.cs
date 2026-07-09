using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public interface IThirdPartyAppealDateConfigurationService
    {
        Task<ThirdPartyAppealDateConfigVm> BuildAsync(
            int? rollId,
            string? rollShortCode,
            string? valuationPeriod,
            string performedBy,
            CancellationToken ct);

        Task SaveStep1AuditAsync(
            ThirdPartyAppealDateConfigVm vm,
            string performedBy,
            CancellationToken ct);

        Task<ThirdPartyAppealResponseDateVm> CalculateResponseDateAsync(
            DateTime? startDate,
            int responseDays,
            CancellationToken ct);
    }
}
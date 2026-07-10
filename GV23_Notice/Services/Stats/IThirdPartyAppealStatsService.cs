using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Stats
{
    public interface IThirdPartyAppealStatsService
    {
        Task<ThirdPartyAppealStatsVm> BuildStatsAsync(
            Guid workflowKey,
            CancellationToken ct);

        Task<ThirdPartyAppealAdminExcelVm> BuildAdminExcelAsync(
           Guid workflowKey,
           string adminKey,
           CancellationToken ct);

        Task<ThirdPartyAppealStatsSendResultVm> SendAdminReportAsync(
    Guid workflowKey,
    string adminKey,
    string performedBy,
    CancellationToken ct);

        Task<ThirdPartyAppealStatsBulkSendResultVm> SendAllAdminReportsAsync(
            Guid workflowKey,
            string performedBy,
            CancellationToken ct);
    }
}

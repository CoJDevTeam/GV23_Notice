namespace GV23_Notice.Services.ThirdPartyApplications
{
    public interface IThirdPartyAppealWorkflowSyncService
    {
        Task<int> SynchronizeAsync(
           int noticeSettingsId,
           string performedBy,
           CancellationToken ct);
    }
}

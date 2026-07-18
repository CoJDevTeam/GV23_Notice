using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    public interface IClaThirdPartyApplicationEmailService
    {
        Task<ThirdPartyAppealSendEmailVm> BuildEmailVmAsync(
            Guid key,
            CancellationToken ct);

        Task<ThirdPartyAppealEmailResultVm> SendAsync(
            Guid key,
            string sentBy,
            CancellationToken ct);

        Task<ThirdPartyAppealEmailProgressVm> GetProgressAsync(
            Guid key,
            CancellationToken ct);
    }
}

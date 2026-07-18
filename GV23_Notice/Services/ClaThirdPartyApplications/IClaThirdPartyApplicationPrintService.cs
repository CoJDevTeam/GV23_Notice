using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    public interface IClaThirdPartyApplicationPrintService
    {
        Task<ThirdPartyAppealPrintVm> BuildPrintVmAsync(Guid key, CancellationToken ct);

        Task<ThirdPartyAppealPrintResultVm> PrintAsync(
            Guid key,
            string printedBy,
            bool forceReprint,
            CancellationToken ct);

        Task<ThirdPartyAppealPrintProgressVm> GetProgressAsync(
            Guid key,
            CancellationToken ct);
    }
}
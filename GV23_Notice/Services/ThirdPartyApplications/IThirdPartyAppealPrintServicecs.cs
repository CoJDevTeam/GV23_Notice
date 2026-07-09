using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public interface IThirdPartyAppealPrintService
    {
        Task<ThirdPartyAppealPrintVm> BuildPrintVmAsync(
            Guid key,
            CancellationToken ct);

        Task<ThirdPartyAppealPrintResultVm> PrintAsync(
            Guid key,
            string printedBy,
            CancellationToken ct);

        Task<ThirdPartyAppealPrintProgressVm> GetProgressAsync(
            Guid key,
            CancellationToken ct);
    }
}

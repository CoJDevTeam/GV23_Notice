using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    public interface IClaThirdPartyAppealPackZipService
    {
        Task<string> BuildAppealPackZipAsync(
            NoticeSettings settings,
            ClaThirdPartyApplicationNotice notice,
            string outputFolder,
            CancellationToken ct);
    }
}

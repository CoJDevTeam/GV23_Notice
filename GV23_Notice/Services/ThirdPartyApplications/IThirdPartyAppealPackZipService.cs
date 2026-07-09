using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public interface IThirdPartyAppealPackZipService
    {
        Task<string> BuildAppealPackZipAsync(
            NoticeSettings settings,
            string appealNo,
            string outputFolder,
            CancellationToken ct);
    }
}

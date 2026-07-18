using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    public interface IClaThirdPartyFormalNoticePdfService
    {
        byte[] BuildPdf(
            NoticeSettings settings,
            ClaThirdPartyApplicationNotice notice);
    }
}

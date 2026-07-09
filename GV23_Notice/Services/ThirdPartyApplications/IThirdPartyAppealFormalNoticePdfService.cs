using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public interface IThirdPartyAppealFormalNoticePdfService
    {
        byte[] BuildPdf(
           NoticeSettings settings,
           ThirdPartyAppealApplicationNotice notice);
    }
}

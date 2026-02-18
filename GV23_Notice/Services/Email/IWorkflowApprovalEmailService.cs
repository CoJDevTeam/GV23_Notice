using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Services.Email
{
    public interface IWorkflowApprovalEmailService
    {
        (string Subject, string BodyHtml) BuildApprovalEmail(NoticeSettings s, RollRegistry roll, string approvedBy, string kickoffUrl);
        (string Subject, string BodyHtml) BuildCorrectionEmail(NoticeSettings s, RollRegistry roll, string requestedBy, string reason);
    }
}

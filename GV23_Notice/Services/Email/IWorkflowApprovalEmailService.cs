using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Services.Email
{
    public interface IWorkflowApprovalEmailService
    {
        /// <summary>
        /// Builds the Step 2 APPROVAL workflow email.
        /// The email must contain a Step 3 kickoff link that is based on the WorkflowKey.
        /// </summary>
        (string Subject, string BodyHtml) BuildApprovalEmail(
            NoticeSettings settings,
            RollRegistry roll,
            string approvedBy,
            Guid workflowKey,
            string kickoffBaseUrl);

        /// <summary>
        /// Builds the Step 2 CORRECTION REQUEST workflow email.
        /// The WorkflowKey is included for audit and reference.
        /// </summary>
        (string Subject, string BodyHtml) BuildCorrectionEmail(
            NoticeSettings settings,
            RollRegistry roll,
            string requestedBy,
            string reason,
            Guid workflowKey);
    }
}

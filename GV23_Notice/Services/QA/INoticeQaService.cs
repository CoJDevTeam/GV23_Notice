using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.QA
{
    public interface INoticeQaService
    {
        Task<bool> RequiresQaAsync(Guid workflowKey, CancellationToken ct);

        Task<bool> IsQaApprovedAsync(Guid workflowKey, CancellationToken ct);

        Task<NoticeQaVm> BuildQaVmAsync(Guid workflowKey, CancellationToken ct);

        Task<int> CreateQaRunAsync(Guid workflowKey, string user, CancellationToken ct);

        Task ApproveQaAsync(Guid workflowKey, int qaRunId, string user, string? comment, CancellationToken ct);

        // New: dynamic QA rule info
        Task<NoticeQaRuleVm> GetQaRuleAsync(Guid workflowKey, CancellationToken ct);
    }
}
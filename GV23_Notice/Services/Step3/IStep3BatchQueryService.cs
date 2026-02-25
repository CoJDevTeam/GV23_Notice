using GV23_Notice.Models.DTOs;
using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Step3
{
    public interface IStep3BatchQueryService
    {
        Task<Step3Step2Vm> BuildAsync(Guid workflowKey, CancellationToken ct);
        Task<S49PendingCountDto> GetS49PendingAsync(int rollId, CancellationToken ct);
    }
}

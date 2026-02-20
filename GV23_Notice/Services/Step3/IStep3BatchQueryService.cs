using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Step3
{
    public interface IStep3BatchQueryService
    {
        Task<Step3Step2Vm> BuildAsync(Guid workflowKey, CancellationToken ct);
    }
}

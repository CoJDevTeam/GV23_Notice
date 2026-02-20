using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Step3
{
    public interface IStep3Step1Service
    {
        Task<Step3Step1Vm> BuildAsync(Guid workflowKey, CancellationToken ct);
    }
}

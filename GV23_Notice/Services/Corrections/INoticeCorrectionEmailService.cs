using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Corrections
{
    public interface INoticeCorrectionEmailService
    {
        Task<CorrectionEmailComposeVm> BuildComposeVmAsync(
            int batchId,
            CancellationToken ct);

        Task SendBatchEmailAsync(
            CorrectionEmailComposeVm vm,
            string sentBy,
            CancellationToken ct);
    }
}

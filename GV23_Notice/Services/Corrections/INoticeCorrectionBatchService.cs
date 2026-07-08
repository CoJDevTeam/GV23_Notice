using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Corrections
{
    public interface INoticeCorrectionBatchService
    {
        Task<int> CreateBatchAsync(
            CorrectionPreviewVm vm,
            string createdBy,
            CancellationToken ct);
    }
}

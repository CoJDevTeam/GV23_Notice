using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Services.Step3
{
    public interface IStep3BatchService
    {
        Task<int> CreateBatchAsync(Guid workflowKey, DateTime batchDate, string createdBy, CancellationToken ct);
        Task<NoticeBatch> CreateBatchAsync(int settingsId, string createdBy, CancellationToken ct);

    }
}

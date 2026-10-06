using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Services.Step3
{
    public interface IStep3BatchService
    {
        Task<int> CreateBatchAsync(Guid workflowKey, DateTime batchDate, string createdBy, CancellationToken ct);
        Task<NoticeBatch> CreateBatchAsync(int settingsId, string createdBy, CancellationToken ct);

        /// <summary>The name the next batch will get (same rule as CreateBatchAsync).</summary>
        Task<string> PeekNextBatchNameAsync(int settingsId, CancellationToken ct);

        /// <summary>How many records go into one batch for this notice.</summary>
        int GetRecordsPerBatch(NoticeKind notice);

    }
}

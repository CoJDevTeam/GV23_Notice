using GV23_Notice.Domain.Rolls;

namespace GV23_Notice.Services.Rolls
{
    public interface IWorkflowRollResolver
    {
        Task<RollRegistry> ResolveByShortCodeAsync(
            string shortCode,
            CancellationToken ct);

        Task<RollRegistry> ResolveByRollIdAsync(
            int rollId,
            CancellationToken ct);
    }
}

using GV23_Notice.Domain.Rolls;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public interface IThirdPartyAppealRollResolver
    {
        Task<RollRegistry> ResolveFromAppealNumberAsync(
            string appealNo,
            CancellationToken ct);
    }
}

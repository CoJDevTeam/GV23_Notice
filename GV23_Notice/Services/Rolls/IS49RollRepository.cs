using GV23_Notice.Models.DTOs;

namespace GV23_Notice.Services.Rolls
{
    public interface IS49RollRepository
    {
        Task<List<string>> PickNextPremiseIdsAsync(int rollId, int top, CancellationToken ct);

        Task<(List<S49RollRowDto> rows, SapContactDto? contact)> LoadPremiseAsync(
            int rollId,
            string premiseId,
            CancellationToken ct);

        Task MarkEmailSentAsync(int rollId, string premiseId, CancellationToken ct);
    }
}

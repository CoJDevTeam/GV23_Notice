using GV23_Notice.Data;
using GV23_Notice.Domain.Rolls;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Rolls
{
    public sealed class WorkflowRollResolver
         : IWorkflowRollResolver
    {
        private readonly AppDbContext _db;

        public WorkflowRollResolver(AppDbContext db)
        {
            _db = db;
        }

        public async Task<RollRegistry> ResolveByShortCodeAsync(
            string shortCode,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(shortCode))
            {
                throw new InvalidOperationException(
                    "Roll short code is required.");
            }

            var normalized =
                NormalizeShortCode(shortCode);

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .Where(x => x.IsActive)
                .FirstOrDefaultAsync(
                    x =>
                        x.ShortCode != null &&
                        x.ShortCode
                            .Replace(" ", "")
                            .ToUpper() == normalized,
                    ct);

            return roll
                ?? throw new InvalidOperationException(
                    $"No active RollRegistry record was found for ShortCode '{shortCode}'.");
        }

        public async Task<RollRegistry> ResolveByRollIdAsync(
            int rollId,
            CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.RollId == rollId &&
                        x.IsActive,
                    ct);

            return roll
                ?? throw new InvalidOperationException(
                    $"No active RollRegistry record was found for RollId '{rollId}'.");
        }

        private static string NormalizeShortCode(
            string value)
        {
            return value
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();
        }
    }
}
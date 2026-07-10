using GV23_Notice.Data;
using GV23_Notice.Domain.Rolls;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public sealed class ThirdPartyAppealRollResolver
       : IThirdPartyAppealRollResolver
    {
        private readonly AppDbContext _db;

        public ThirdPartyAppealRollResolver(
            AppDbContext db)
        {
            _db = db;
        }

        public async Task<RollRegistry> ResolveFromAppealNumberAsync(
            string appealNo,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(appealNo))
            {
                throw new InvalidOperationException(
                    "Appeal number is required to resolve the roll.");
            }

            var shortCode = ResolveShortCode(appealNo);

            var normalizedShortCode =
                NormalizeShortCode(shortCode);

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .Where(x => x.IsActive)
                .FirstOrDefaultAsync(
                    x =>
                        x.ShortCode != null &&
                        x.ShortCode
                            .Replace(" ", "")
                            .ToUpper() ==
                        normalizedShortCode,
                    ct);

            if (roll == null)
            {
                throw new InvalidOperationException(
                    $"No active RollRegistry record was found for Appeal Number '{appealNo}' and ShortCode '{shortCode}'.");
            }

            return roll;
        }

        private static string ResolveShortCode(
            string appealNo)
        {
            var value = appealNo
                .Trim()
                .ToUpperInvariant();

            if (value.StartsWith(
                "APP-GV23-SUP3-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 3";
            }

            if (value.StartsWith(
                "APP-GV23-SUP2-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 2";
            }

            if (value.StartsWith(
                "APP-GV23-SUP1-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 1";
            }

            if (value.StartsWith(
                "APP-GV23-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "GV23";
            }

            throw new InvalidOperationException(
                $"The Appeal Number '{appealNo}' does not match a supported TPA roll pattern.");
        }

        private static string NormalizeShortCode(
            string shortCode)
        {
            return shortCode
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();
        }
    }
}
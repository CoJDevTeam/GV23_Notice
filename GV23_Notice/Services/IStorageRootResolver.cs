using GV23_Notice.Data;
using GV23_Notice.Domain.Rolls;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services
{
    public enum DataDomain
    {
        Objection = 1,
        Appeal = 2
    }

    public interface IStorageRootResolver
    {
        Task<RollRegistry> GetRollAsync(int rollId, CancellationToken ct);
        Task<string> GetRootAsync(int rollId, DataDomain domain, CancellationToken ct);
        Task<string> GetSignatureFolderAsync(int rollId, DataDomain domain, CancellationToken ct);
    }

    public sealed class StorageRootResolver : IStorageRootResolver
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _cfg;

        public StorageRootResolver(AppDbContext db, IConfiguration cfg)
        {
            _db = db;
            _cfg = cfg;
        }

        public async Task<RollRegistry> GetRollAsync(int rollId, CancellationToken ct)
        {
            var roll = await _db.RollRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.RollId == rollId, ct);
            if (roll is null) throw new InvalidOperationException($"RollRegistry not found for RollId={rollId}");
            if (!roll.IsActive) throw new InvalidOperationException($"Roll is not active: {roll.ShortCode}");
            return roll;
        }

        public async Task<string> GetRootAsync(int rollId, DataDomain domain, CancellationToken ct)
        {
            var roll = await GetRollAsync(rollId, ct);

            var section = domain == DataDomain.Objection
                ? "Storage:ObjectionRootsByShortCode"
                : "Storage:AppealRootsByShortCode";

            var root = _cfg.GetSection(section)[roll.ShortCode];

            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException($"Storage root not configured for {domain} roll '{roll.ShortCode}' in appsettings.json");

            return root;
        }

        public async Task<string> GetSignatureFolderAsync(int rollId, DataDomain domain, CancellationToken ct)
        {
            var root = await GetRootAsync(rollId, domain, ct);
            var sigFolderName = _cfg["Storage:SignatureFolderName"] ?? "Signature Folder";
            return Path.Combine(root, sigFolderName);
        }
    }
}


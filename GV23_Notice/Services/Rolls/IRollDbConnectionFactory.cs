using GV23_Notice.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Rolls
{
    public interface IRollDbConnectionFactory
    {
        Task<SqlConnection> OpenAsync(int rollId, CancellationToken ct);
    }

    public sealed class RollDbConnectionFactory : IRollDbConnectionFactory
    {
        private readonly AppDbContext _centralDb;
        private readonly IConfiguration _cfg;

        public RollDbConnectionFactory(AppDbContext centralDb, IConfiguration cfg)
        {
            _centralDb = centralDb;
            _cfg = cfg;
        }

        public async Task<SqlConnection> OpenAsync(int rollId, CancellationToken ct)
        {
            var roll = await _centralDb.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == rollId, ct);

            if (roll is null) throw new InvalidOperationException($"Roll not found. RollId={rollId}");
            if (!roll.IsActive) throw new InvalidOperationException($"Roll inactive: {roll.ShortCode}");

            var baseCs = _cfg["RollDb:BaseSqlConnection"];
            if (string.IsNullOrWhiteSpace(baseCs))
                throw new InvalidOperationException("Missing RollDb:BaseSqlConnection in appsettings.json");

            // Build: base + Database=<RollRegistry.SourceDb>
            var builder = new SqlConnectionStringBuilder(baseCs);

            // Use SourceDb as the database name (as per your RollRegistry inserts)
            builder.InitialCatalog = roll.SourceDb;

            var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync(ct);
            return conn;
        }
    }
}


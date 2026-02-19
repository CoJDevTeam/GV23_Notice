using GV23_Notice.Data;
using GV23_Notice.Services.Rolls;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GV23_Notice.Services
{
    public interface IHolidayService
    {
        Task<HashSet<DateOnly>> GetHolidaysAsync(int rollId, DateOnly from, DateOnly to, CancellationToken ct);
        Task<bool> IsHolidayAsync(int rollId, DateOnly date, CancellationToken ct);
    }

    public sealed class HolidayService : IHolidayService
    {
        private readonly AppDbContext _centralDb;
        private readonly IRollDbConnectionFactory _connFactory;
        private readonly IConfiguration _cfg;
        private readonly IMemoryCache _cache;

        public HolidayService(
            AppDbContext centralDb,
            IRollDbConnectionFactory connFactory,
            IConfiguration cfg,
            IMemoryCache cache)
        {
            _centralDb = centralDb;
            _connFactory = connFactory;
            _cfg = cfg;
            _cache = cache;
        }

        private sealed record HolidayTableCfg(string Table, string DateColumn);

        private async Task<(string SourceDb, HolidayTableCfg Cfg)> GetHolidayTableCfgAsync(int rollId, CancellationToken ct)
        {
            var roll = await _centralDb.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == rollId, ct);

            if (roll is null)
                throw new InvalidOperationException($"Roll not found. RollId={rollId}");

            if (string.IsNullOrWhiteSpace(roll.SourceDb))
                throw new InvalidOperationException($"Roll SourceDb is missing. RollId={rollId}");

            var sectionPath = $"RollDb:HolidayTablesBySourceDb:{roll.SourceDb}";
            var table = _cfg[$"{sectionPath}:Table"];
            var col = _cfg[$"{sectionPath}:DateColumn"];

            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(col))
                throw new InvalidOperationException(
                    $"Holiday table config missing for SourceDb '{roll.SourceDb}' in appsettings.json (Table/DateColumn).");

            return (roll.SourceDb, new HolidayTableCfg(table.Trim(), col.Trim()));
        }

        public async Task<HashSet<DateOnly>> GetHolidaysAsync(int rollId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            if (to < from) (from, to) = (to, from);

            // Cache per roll + date span (safe + simple)
            var key = $"holidays:{rollId}:{from:yyyyMMdd}:{to:yyyyMMdd}";
            if (_cache.TryGetValue(key, out HashSet<DateOnly>? cached) && cached is not null)
                return cached;

            var (sourceDb, cfg) = await GetHolidayTableCfgAsync(rollId, ct);

            // ✅ Use your updated factory (Create(sourceDb)) — no OpenAsync on the factory
            await using var conn = _connFactory.Create(sourceDb);
            await conn.OpenAsync(ct);

            // IMPORTANT: table/column come from config (trusted), not user input.
            // Still: wrap identifiers in [] and do NOT parameterize identifiers.
            var sql = $@"
SELECT [{cfg.DateColumn}]
FROM dbo.[{cfg.Table}]
WHERE [{cfg.DateColumn}] >= @FromDate AND [{cfg.DateColumn}] <= @ToDate;";

            await using var cmd = new SqlCommand(sql, conn)
            {
                CommandTimeout = 60
            };

            // Use typed parameters (avoid AddWithValue surprises)
            cmd.Parameters.Add(new SqlParameter("@FromDate", System.Data.SqlDbType.DateTime2)
            {
                Value = from.ToDateTime(TimeOnly.MinValue)
            });
            cmd.Parameters.Add(new SqlParameter("@ToDate", System.Data.SqlDbType.DateTime2)
            {
                Value = to.ToDateTime(TimeOnly.MinValue)
            });

            var set = new HashSet<DateOnly>();

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0)) continue;

                // Handles Date/DateTime/DateTime2 columns
                var dt = reader.GetDateTime(0).Date;
                set.Add(DateOnly.FromDateTime(dt));
            }

            _cache.Set(key, set, TimeSpan.FromMinutes(30));
            return set;
        }

        public async Task<bool> IsHolidayAsync(int rollId, DateOnly date, CancellationToken ct)
        {
            var set = await GetHolidaysAsync(rollId, date, date, ct);
            return set.Contains(date);
        }
    }
}

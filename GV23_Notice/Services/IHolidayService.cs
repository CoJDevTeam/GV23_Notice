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

        private async Task<HolidayTableCfg> GetHolidayTableCfgAsync(int rollId, CancellationToken ct)
        {
            var roll = await _centralDb.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == rollId, ct);

            if (roll is null) throw new InvalidOperationException($"Roll not found. RollId={rollId}");

            var sectionPath = $"RollDb:HolidayTablesBySourceDb:{roll.SourceDb}";
            var table = _cfg[$"{sectionPath}:Table"];
            var col = _cfg[$"{sectionPath}:DateColumn"];

            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(col))
                throw new InvalidOperationException($"Holiday table config missing for SourceDb '{roll.SourceDb}' in appsettings.json");

            return new HolidayTableCfg(table, col);
        }

        public async Task<HashSet<DateOnly>> GetHolidaysAsync(int rollId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            if (to < from) (from, to) = (to, from);

            // Cache per roll + year span (simple and very effective)
            var key = $"holidays:{rollId}:{from:yyyyMMdd}:{to:yyyyMMdd}";
            if (_cache.TryGetValue(key, out HashSet<DateOnly> cached))
                return cached;

            var cfg = await GetHolidayTableCfgAsync(rollId, ct);

            await using var conn = await _connFactory.OpenAsync(rollId, ct);

            // IMPORTANT: table/column come from config (trusted), not user input.
            var sql = $@"
SELECT [{cfg.DateColumn}]
FROM dbo.[{cfg.Table}]
WHERE [{cfg.DateColumn}] >= @FromDate AND [{cfg.DateColumn}] <= @ToDate;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@FromDate", from.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@ToDate", to.ToDateTime(TimeOnly.MinValue));

            var set = new HashSet<DateOnly>();

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0)) continue;

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


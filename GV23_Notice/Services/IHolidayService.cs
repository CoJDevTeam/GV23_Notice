using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GV23_Notice.Services
{
    public interface IHolidayService
    {
        /// <summary>
        /// Returns all public holidays in [from, to] for the given valuation period
        /// (rows with NULL ValuationPeriodCode apply to all periods).
        /// </summary>
        Task<HashSet<DateOnly>> GetHolidaysAsync(
            string? valuationPeriodCode,
            DateOnly from, DateOnly to,
            CancellationToken ct);

        Task<bool> IsHolidayAsync(
            string? valuationPeriodCode,
            DateOnly date,
            CancellationToken ct);

        // Convenience overloads — resolve ValuationPeriodCode from NoticeSettings.Id
        Task<HashSet<DateOnly>> GetHolidaysBySettingsAsync(
            int settingsId, DateOnly from, DateOnly to, CancellationToken ct);

        Task<bool> IsHolidayBySettingsAsync(
            int settingsId, DateOnly date, CancellationToken ct);
    }

    public sealed class HolidayService : IHolidayService
    {
        private readonly AppDbContext _db;
        private readonly IMemoryCache _cache;

        public HolidayService(AppDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public async Task<HashSet<DateOnly>> GetHolidaysAsync(
            string? valuationPeriodCode,
            DateOnly from, DateOnly to,
            CancellationToken ct)
        {
            if (to < from) (from, to) = (to, from);

            var cacheKey = $"holidays:{valuationPeriodCode ?? "ANY"}:{from:yyyyMMdd}:{to:yyyyMMdd}";
            if (_cache.TryGetValue(cacheKey, out HashSet<DateOnly>? cached) && cached is not null)
                return cached;

            var rows = await _db.PublicHolidays
                .AsNoTracking()
                .Where(h =>
                    h.HolidayDate >= from && h.HolidayDate <= to &&
                    (h.ValuationPeriodCode == null ||
                     h.ValuationPeriodCode == valuationPeriodCode))
                .Select(h => h.HolidayDate)
                .ToListAsync(ct);

            var set = new HashSet<DateOnly>(rows);
            _cache.Set(cacheKey, set, TimeSpan.FromMinutes(30));
            return set;
        }

        public async Task<bool> IsHolidayAsync(
            string? valuationPeriodCode,
            DateOnly date,
            CancellationToken ct)
        {
            var set = await GetHolidaysAsync(valuationPeriodCode, date, date, ct);
            return set.Contains(date);
        }

        public async Task<HashSet<DateOnly>> GetHolidaysBySettingsAsync(
            int settingsId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            var code = await _db.NoticeSettings
                .AsNoTracking()
                .Where(s => s.Id == settingsId)
                .Select(s => s.ValuationPeriodCode)
                .FirstOrDefaultAsync(ct);

            return await GetHolidaysAsync(code, from, to, ct);
        }

        public async Task<bool> IsHolidayBySettingsAsync(
            int settingsId, DateOnly date, CancellationToken ct)
        {
            var set = await GetHolidaysBySettingsAsync(settingsId, date, date, ct);
            return set.Contains(date);
        }
    }
}
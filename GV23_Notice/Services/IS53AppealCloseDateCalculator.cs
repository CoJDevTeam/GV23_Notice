namespace GV23_Notice.Services
{
    public interface IS53AppealCloseDateCalculator
    {
        /// <summary>
        /// LetterDate + calendarDays (default 45), then push forward past
        /// weekends and public holidays to the next working day.
        /// </summary>
        Task<DateOnly> CalculateAsync(
            string? valuationPeriodCode,
            DateOnly letterDate,
            int calendarDays = 45,
            CancellationToken ct = default);

        /// <summary>Overload that resolves the period code from NoticeSettings.</summary>
        Task<DateOnly> CalculateBySettingsAsync(
            int settingsId,
            DateOnly letterDate,
            int calendarDays = 45,
            CancellationToken ct = default);

        Task<bool> IsWorkingDayAsync(
            string? valuationPeriodCode,
            DateOnly date,
            CancellationToken ct = default);
    }

    public sealed class S53AppealCloseDateCalculator : IS53AppealCloseDateCalculator
    {
        private readonly IHolidayService _holidays;

        public S53AppealCloseDateCalculator(IHolidayService holidays)
        {
            _holidays = holidays;
        }

        public async Task<DateOnly> CalculateAsync(
            string? valuationPeriodCode,
            DateOnly letterDate,
            int calendarDays = 45,
            CancellationToken ct = default)
        {
            // 1) Add calendar days (includes weekends & holidays)
            var candidate = letterDate.AddDays(calendarDays);

            // 2) Push to the next working day if it falls on a non-working day
            while (!await IsWorkingDayAsync(valuationPeriodCode, candidate, ct))
                candidate = candidate.AddDays(1);

            return candidate;
        }

        public async Task<DateOnly> CalculateBySettingsAsync(
            int settingsId,
            DateOnly letterDate,
            int calendarDays = 45,
            CancellationToken ct = default)
        {
            var set = await _holidays.GetHolidaysBySettingsAsync(
                settingsId, letterDate, letterDate.AddDays(calendarDays + 14), ct);

            // Inline the same logic using the pre-fetched set (avoids extra DB calls)
            var candidate = letterDate.AddDays(calendarDays);
            while (IsWeekend(candidate) || set.Contains(candidate))
                candidate = candidate.AddDays(1);

            return candidate;
        }

        public async Task<bool> IsWorkingDayAsync(
            string? valuationPeriodCode,
            DateOnly date,
            CancellationToken ct = default)
        {
            if (IsWeekend(date)) return false;
            return !await _holidays.IsHolidayAsync(valuationPeriodCode, date, ct);
        }

        private static bool IsWeekend(DateOnly d) =>
            d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }
}
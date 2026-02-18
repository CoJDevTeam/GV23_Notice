namespace GV23_Notice.Services
{
    public interface IS53AppealCloseDateCalculator
    {
        Task<DateOnly> CalculateAsync(int rollId, DateOnly letterDate, int calendarDays = 45, CancellationToken ct = default);
        Task<bool> IsWorkingDayAsync(int rollId, DateOnly date, CancellationToken ct = default);
    }

    public sealed class S53AppealCloseDateCalculator : IS53AppealCloseDateCalculator
    {
        private readonly IHolidayService _holidays;

        public S53AppealCloseDateCalculator(IHolidayService holidays)
        {
            _holidays = holidays;
        }

        public async Task<DateOnly> CalculateAsync(int rollId, DateOnly letterDate, int calendarDays = 45, CancellationToken ct = default)
        {
            // 1) calendar days: includes weekends and holidays
            var candidate = letterDate.AddDays(calendarDays);

            // 2) if it lands on non-working day, push to next working day
            while (!await IsWorkingDayAsync(rollId, candidate, ct))
            {
                candidate = candidate.AddDays(1);
            }

            return candidate;
        }

        public async Task<bool> IsWorkingDayAsync(int rollId, DateOnly date, CancellationToken ct = default)
        {
            var dow = date.DayOfWeek;
            if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday)
                return false;

            if (await _holidays.IsHolidayAsync(rollId, date, ct))
                return false;

            return true;
        }
    }
}


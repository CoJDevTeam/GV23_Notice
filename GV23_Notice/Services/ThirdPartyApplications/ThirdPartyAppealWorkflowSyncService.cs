using GV23_Notice.Data;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public sealed class ThirdPartyAppealWorkflowSyncService
         : IThirdPartyAppealWorkflowSyncService
    {
        private readonly AppDbContext _db;
        private readonly IThirdPartyAppealRollResolver _rollResolver;

        public ThirdPartyAppealWorkflowSyncService(
            AppDbContext db,
            IThirdPartyAppealRollResolver rollResolver)
        {
            _db = db;
            _rollResolver = rollResolver;
        }

        public async Task<int> SynchronizeAsync(
            int noticeSettingsId,
            string performedBy,
            CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .FirstOrDefaultAsync(
                    x => x.Id == noticeSettingsId,
                    ct)
                ?? throw new InvalidOperationException(
                    "Notice settings were not found.");

            var rows = await _db
                .ThirdPartyAppealApplicationNotices
                .Where(x =>
                    x.NoticeSettingsId == null ||
                    x.NoticeSettingsId == noticeSettingsId)
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Appeal_No))
                .ToListAsync(ct);

            var updated = 0;

            foreach (var row in rows)
            {
                var roll = await _rollResolver
                    .ResolveFromAppealNumberAsync(
                        row.Appeal_No!,
                        ct);

                row.NoticeSettingsId = settings.Id;
                row.RollId = roll.RollId;
                row.RollShortCode = roll.ShortCode;
                row.ValuationPeriod = roll.Name;

                row.UpdatedAt = DateTime.Now;
                row.UpdatedBy = performedBy;

                updated++;
            }

            await _db.SaveChangesAsync(ct);

            return updated;
        }
    }
}

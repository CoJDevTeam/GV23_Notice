using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
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

            var updatedBy = string.IsNullOrWhiteSpace(performedBy)
                ? "SYSTEM-WORKFLOW-SYNC"
                : performedBy.Trim();

            return settings.Notice switch
            {
                NoticeKind.TPA => await SynchronizeTpaAsync(
                    settings.Id,
                    updatedBy,
                    ct),

                NoticeKind.CLA_TPA => await SynchronizeClaAsync(
                    settings.Id,
                    settings.RollId,
                    updatedBy,
                    ct),

                _ => throw new InvalidOperationException(
                    $"Workflow synchronisation is not supported for notice type '{settings.Notice}'.")
            };
        }

        private async Task<int> SynchronizeTpaAsync(
            int noticeSettingsId,
            string performedBy,
            CancellationToken ct)
        {
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
                ct.ThrowIfCancellationRequested();

                var roll = await _rollResolver
                    .ResolveFromAppealNumberAsync(
                        row.Appeal_No!,
                        ct);

                var changed =
                    row.NoticeSettingsId != noticeSettingsId ||
                    row.RollId != roll.RollId ||
                    !string.Equals(
                        row.RollShortCode,
                        roll.ShortCode,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        row.ValuationPeriod,
                        roll.Name,
                        StringComparison.OrdinalIgnoreCase);

                if (!changed)
                    continue;

                row.NoticeSettingsId = noticeSettingsId;
                row.RollId = roll.RollId;
                row.RollShortCode = roll.ShortCode;
                row.ValuationPeriod = roll.Name;

                row.UpdatedAt = DateTime.Now;
                row.UpdatedBy = performedBy;

                updated++;
            }

            if (updated > 0)
                await _db.SaveChangesAsync(ct);

            return updated;
        }

        private async Task<int> SynchronizeClaAsync(
            int noticeSettingsId,
            int settingsRollId,
            string performedBy,
            CancellationToken ct)
        {
            /*
             * CLA numbers do not use the APP-GV23-* appeal-number pattern.
             * Therefore, resolve the CLA roll from the NoticeSettings RollId.
             */
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.RollId == settingsRollId &&
                        x.IsActive,
                    ct)
                ?? throw new InvalidOperationException(
                    $"No active RollRegistry record was found for RollId '{settingsRollId}'.");

            var rows = await _db
                .ClaThirdPartyApplicationNotices
                .Where(x => x.IsActive)
                .Where(x =>
                    x.NoticeSettingsId == null ||
                    x.NoticeSettingsId == 0 ||
                    x.NoticeSettingsId == noticeSettingsId)
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.ClaNumber))
                .ToListAsync(ct);

            var updated = 0;

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();

                var changed =
                    row.NoticeSettingsId != noticeSettingsId ||
                    row.RollId != roll.RollId ||
                    !string.Equals(
                        row.RollShortCode,
                        roll.ShortCode,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        row.ValuationPeriod,
                        roll.Name,
                        StringComparison.OrdinalIgnoreCase);

                if (!changed)
                    continue;

                row.NoticeSettingsId = noticeSettingsId;
                row.RollId = roll.RollId;
                row.RollShortCode = roll.ShortCode;
                row.ValuationPeriod = roll.Name;

                row.UpdatedAtUtc = DateTime.UtcNow;
                row.UpdatedBy = performedBy;

                updated++;
            }

            if (updated > 0)
                await _db.SaveChangesAsync(ct);

            return updated;
        }
    }
}
using GV23_Notice.Data;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services
{
    public interface INoticeSettingsService
    {
        Task<NoticeSettings> CreateDraftAsync(
            int rollId,
            NoticeKind notice,
            BatchMode mode,
            string user,
            CancellationToken ct);

        Task<NoticeSettings?> GetLatestDraftOrApprovedAsync(
            int rollId,
            NoticeKind notice,
            BatchMode mode,
            CancellationToken ct);

        Task<NoticeSettings?> GetByIdAsync(
            int id,
            CancellationToken ct);

        Task<NoticeSettings?> GetByIdForUpdateAsync(
            int id,
            CancellationToken ct);

        Task<NoticeSettings> SaveDraftAsync(
            NoticeSettings draft,
            CancellationToken ct);

        Task ConfirmAsync(
            int settingsId,
            string user,
            string? notes,
            CancellationToken ct);

        Task ApproveAsync(
            int settingsId,
            string user,
            string? notes,
            CancellationToken ct);
    }

    public sealed class NoticeSettingsService
        : INoticeSettingsService
    {
        private const int ClaRepresentationDays = 30;

        private readonly AppDbContext _db;

        public NoticeSettingsService(
            AppDbContext db)
        {
            _db = db;
        }

        public async Task<NoticeSettings?> GetByIdAsync(
            int id,
            CancellationToken ct)
        {
            return await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    ct);
        }

        public async Task<NoticeSettings?> GetByIdForUpdateAsync(
            int id,
            CancellationToken ct)
        {
            return await _db.NoticeSettings
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    ct);
        }

        public async Task<NoticeSettings?>
            GetLatestDraftOrApprovedAsync(
                int rollId,
                NoticeKind notice,
                BatchMode mode,
                CancellationToken ct)
        {
            var rollCode = await ResolveRollCodeAsync(
                rollId,
                ct);

            return await _db.NoticeSettings
                .AsNoTracking()
                .Where(x =>
                    x.Roll == rollCode &&
                    x.Notice == notice &&
                    x.Mode == mode)
                .OrderByDescending(x => x.Version)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<NoticeSettings> CreateDraftAsync(
            int rollId,
            NoticeKind notice,
            BatchMode mode,
            string user,
            CancellationToken ct)
        {
            await using var tx =
                await _db.Database.BeginTransactionAsync(ct);

            var rollCode = await ResolveRollCodeAsync(
                rollId,
                ct);

            var maxVersion = await _db.NoticeSettings
                .Where(x =>
                    x.Roll == rollCode &&
                    x.Notice == notice &&
                    x.Mode == mode)
                .Select(x => (int?)x.Version)
                .MaxAsync(ct) ?? 0;

            var today = DateTime.Today;

            var draft = new NoticeSettings
            {
                RollId = rollId,
                Roll = rollCode,
                Notice = notice,
                Mode = mode,
                Version = maxVersion + 1,

                LetterDate = today,
                BatchDate = today,

                IsConfirmed = false,
                IsApproved = false
            };

            ApplyNoticeSpecificDates(draft);

            _db.NoticeSettings.Add(draft);

            _db.NoticeApprovalLogs.Add(
                new NoticeApprovalLog
                {
                    NoticeSettings = draft,
                    Action = ApprovalAction.Created,
                    PerformedBy = user,
                    Notes =
                        $"Draft created v{draft.Version}"
                });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return draft;
        }

        public async Task<NoticeSettings> SaveDraftAsync(
            NoticeSettings draft,
            CancellationToken ct)
        {
            if (draft.Id <= 0)
            {
                throw new InvalidOperationException(
                    "Cannot save draft because NoticeSettings.Id is not set.");
            }

            var tracked = await _db.NoticeSettings
                .FirstOrDefaultAsync(
                    x => x.Id == draft.Id,
                    ct);

            if (tracked is null)
            {
                throw new InvalidOperationException(
                    $"NoticeSettings with Id {draft.Id} was not found.");
            }

            if (tracked.IsApproved)
            {
                throw new InvalidOperationException(
                    "Approved date configuration cannot be modified.");
            }

            /*
             * Copy the values submitted by the Date Configuration form.
             */
            _db.Entry(tracked)
                .CurrentValues
                .SetValues(draft);

            /*
             * Apply the CLA-specific date calculation after SetValues so
             * users cannot submit an incorrect representation close date.
             */
            ApplyNoticeSpecificDates(tracked);

            ValidateDates(tracked);

            await _db.SaveChangesAsync(ct);

            return tracked;
        }

        public async Task ConfirmAsync(
            int settingsId,
            string user,
            string? notes,
            CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .FirstOrDefaultAsync(
                    x => x.Id == settingsId,
                    ct)
                ?? throw new InvalidOperationException(
                    "Settings not found.");

            if (settings.IsApproved)
            {
                throw new InvalidOperationException(
                    "The date configuration has already been approved.");
            }

            ApplyNoticeSpecificDates(settings);
            ValidateDates(settings);

            settings.IsConfirmed = true;
            settings.ConfirmedBy = user;
            settings.ConfirmedAtUtc = DateTime.UtcNow;

            _db.NoticeApprovalLogs.Add(
                new NoticeApprovalLog
                {
                    NoticeSettingsId = settings.Id,
                    Action = ApprovalAction.Confirmed,
                    PerformedBy = user,
                    Notes = notes
                });

            await _db.SaveChangesAsync(ct);
        }

        public async Task ApproveAsync(
            int settingsId,
            string user,
            string? notes,
            CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .FirstOrDefaultAsync(
                    x => x.Id == settingsId,
                    ct)
                ?? throw new InvalidOperationException(
                    "Settings not found.");

            if (!settings.IsConfirmed)
            {
                throw new InvalidOperationException(
                    "Confirm first before Approve.");
            }

            if (settings.IsApproved)
            {
                throw new InvalidOperationException(
                    "The date configuration has already been approved.");
            }

            ApplyNoticeSpecificDates(settings);
            ValidateDates(settings);

            settings.IsApproved = true;
            settings.ApprovedBy = user;
            settings.ApprovedAtUtc = DateTime.UtcNow;

            _db.NoticeApprovalLogs.Add(
                new NoticeApprovalLog
                {
                    NoticeSettingsId = settings.Id,
                    Action = ApprovalAction.Approved,
                    PerformedBy = user,
                    Notes = notes
                });

            await _db.SaveChangesAsync(ct);
        }

        private static void ApplyNoticeSpecificDates(
            NoticeSettings settings)
        {
            if (settings.Notice != NoticeKind.CLA_TPA)
            {
                return;
            }

            var letterDate = settings.LetterDate.Date;

            /*
             * CLA gives the property owner 30 calendar days to submit
             * written representations.
             */
            settings.LetterDate = letterDate;
            settings.BatchDate = letterDate;

            settings.ObjectionStartDate = letterDate;

            settings.ObjectionEndDate =
                letterDate.AddDays(
                    ClaRepresentationDays);

            /*
             * CLA-TPA does not use an extension date during initial
             * configuration.
             */
            settings.ExtensionDate = null;
        }

        private static void ValidateDates(
            NoticeSettings settings)
        {
            if (settings.LetterDate == default)
            {
                throw new InvalidOperationException(
                    "Letter Date is required.");
            }

            if (settings.Notice == NoticeKind.CLA_TPA)
            {
                var expectedClosingDate =
                    settings.LetterDate.Date.AddDays(
                        ClaRepresentationDays);

                if (!settings.ObjectionStartDate.HasValue)
                {
                    throw new InvalidOperationException(
                        "Representation Start Date is required for CLA-TPA.");
                }

                if (!settings.ObjectionEndDate.HasValue)
                {
                    throw new InvalidOperationException(
                        "Representation Closing Date is required for CLA-TPA.");
                }

                if (settings.ObjectionStartDate.Value.Date !=
                    settings.LetterDate.Date)
                {
                    throw new InvalidOperationException(
                        "The CLA representation start date must match the Letter Date.");
                }

                if (settings.ObjectionEndDate.Value.Date !=
                    expectedClosingDate)
                {
                    throw new InvalidOperationException(
                        $"The CLA representation closing date must be {ClaRepresentationDays} calendar days after the Letter Date.");
                }
            }
        }

        private async Task<RollCode> ResolveRollCodeAsync(
            int rollId,
            CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.RollId == rollId,
                    ct);

            if (roll is null)
            {
                throw new InvalidOperationException(
                    $"RollRegistry record not found for RollId {rollId}.");
            }

            var code = (roll.ShortCode ?? "")
                .Trim()
                .ToUpperInvariant();

            return code switch
            {
                "GV23" =>
                    RollCode.GV23,

                "SUPP1" =>
                    RollCode.SUPP1,

                "SUPP 1" =>
                    RollCode.SUPP1,

                "SUPP2" =>
                    RollCode.SUPP2,

                "SUPP 2" =>
                    RollCode.SUPP2,

                "SUPP3" =>
                    RollCode.SUPP3,

                "SUPP 3" =>
                    RollCode.SUPP3,

                "QUERY" =>
                    RollCode.QUERY,

                _ => throw new InvalidOperationException(
                    $"Unsupported RollRegistry.ShortCode '{roll.ShortCode}' for RollId {rollId}.")
            };
        }
    }
}
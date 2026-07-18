using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Step3
{
    public interface IStep3PrintQueryService
    {
        Task<Step3PrintVm> BuildPrintVmAsync(Guid workflowKey, CancellationToken ct);
        Task<Step3SendEmailVm> BuildEmailVmAsync(Guid workflowKey, CancellationToken ct);
    }

    public sealed class Step3PrintQueryService : IStep3PrintQueryService
    {
        private readonly AppDbContext _db;

        public Step3PrintQueryService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Step3PrintVm> BuildPrintVmAsync(Guid workflowKey, CancellationToken ct)
        {
            var (s, roll) = await ResolveAsync(workflowKey, ct);

            if (s.Notice == NoticeKind.TPA)
            {
                var tpaRollSummary = await SynchronizeTpaRollMetadataAsync(
                    s,
                    ct);

                return await BuildTpaPrintVmAsync(
                    s,
                    tpaRollSummary,
                    workflowKey,
                    ct);
            }

            if (s.Notice == NoticeKind.CLA_TPA)
            {
                var claRollSummary = await SynchronizeClaRollMetadataAsync(
                    s,
                    ct);

                return await BuildClaPrintVmAsync(
                    s,
                    claRollSummary,
                    workflowKey,
                    ct);
            }

            var batches = await _db.NoticeBatches.AsNoTracking()
                .Where(b => b.WorkflowKey == workflowKey
                         && b.RollId == s.RollId
                         && b.Notice == s.Notice
                         && b.BatchKind == "STEP3")
                .OrderBy(b => b.Id)
                .ToListAsync(ct);

            var batchIds = batches.Select(b => b.Id).ToList();

            var statusCounts = await _db.NoticeRunLogs.AsNoTracking()
                .Where(r => batchIds.Contains(r.NoticeBatchId))
                .GroupBy(r => new { r.NoticeBatchId, r.Status })
                .Select(g => new { g.Key.NoticeBatchId, g.Key.Status, Count = g.Count() })
                .ToListAsync(ct);

            var rows = batches.Select(b =>
            {
                var counts = statusCounts.Where(x => x.NoticeBatchId == b.Id).ToList();

                return new Step3PrintBatchRowVm
                {
                    BatchId = b.Id,
                    BatchName = b.BatchName,
                    BatchDate = b.BatchDate,
                    NumberOfRecords = b.NumberOfRecords,
                    CreatedBy = b.CreatedBy,
                    CreatedAtUtc = b.CreatedAtUtc,
                    IsApproved = b.IsApproved,
                    GeneratedCount = counts.FirstOrDefault(x => x.Status == RunStatus.Generated)?.Count ?? 0,
                    PrintedCount = counts.FirstOrDefault(x => x.Status == RunStatus.Printed)?.Count ?? 0,
                    FailedCount = counts.FirstOrDefault(x => x.Status == RunStatus.Failed)?.Count ?? 0,
                    SentCount = counts.FirstOrDefault(x => x.Status == RunStatus.Sent)?.Count ?? 0
                };
            }).ToList();

            return new Step3PrintVm
            {
                WorkflowKey = workflowKey,
                SettingsId = s.Id,
                RollId = s.RollId,
                RollShortCode = roll?.ShortCode ?? "",
                RollName = roll?.Name ?? s.RollName ?? "",
                Notice = s.Notice,
                VersionText = $"V{s.Version}",
                LetterDate = s.LetterDate,
                FinancialYearsText = s.FinancialYearsText,
                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                SignaturePath = s.SignaturePath,

                TotalBatches = batches.Count,
                TotalRecordsBatched = batches.Sum(b => b.NumberOfRecords),
                TotalPrinted = rows.Sum(r => r.PrintedCount),
                TotalFailed = rows.Sum(r => r.FailedCount),
                Batches = rows,

                IsS52 = s.Notice == NoticeKind.S52,
                S52IsReview = s.IsSection52Review,
                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate
            };
        }

        public async Task<Step3SendEmailVm> BuildEmailVmAsync(Guid workflowKey, CancellationToken ct)
        {
            var (s, roll) = await ResolveAsync(workflowKey, ct);

            if (s.Notice == NoticeKind.TPA)
            {
                var tpaRollSummary = await SynchronizeTpaRollMetadataAsync(
                    s,
                    ct);

                return await BuildTpaEmailVmAsync(
                    s,
                    tpaRollSummary,
                    workflowKey,
                    ct);
            }

            if (s.Notice == NoticeKind.CLA_TPA)
            {
                var claRollSummary = await SynchronizeClaRollMetadataAsync(
                    s,
                    ct);

                return await BuildClaEmailVmAsync(
                    s,
                    claRollSummary,
                    workflowKey,
                    ct);
            }

            var batches = await _db.NoticeBatches.AsNoTracking()
                .Where(b => b.WorkflowKey == workflowKey
                         && b.RollId == s.RollId
                         && b.Notice == s.Notice
                         && b.BatchKind == "STEP3")
                .OrderBy(b => b.Id)
                .ToListAsync(ct);

            var batchIds = batches.Select(b => b.Id).ToList();

            var statusCounts = await _db.NoticeRunLogs.AsNoTracking()
                .Where(r => batchIds.Contains(r.NoticeBatchId))
                .GroupBy(r => new { r.NoticeBatchId, r.Status })
                .Select(g => new { g.Key.NoticeBatchId, g.Key.Status, Count = g.Count() })
                .ToListAsync(ct);

            var rows = batches.Select(b =>
            {
                var counts = statusCounts.Where(x => x.NoticeBatchId == b.Id).ToList();

                return new Step3EmailBatchRowVm
                {
                    BatchId = b.Id,
                    BatchName = b.BatchName,
                    BatchDate = b.BatchDate,
                    CreatedBy = b.CreatedBy,
                    CreatedAtUtc = b.CreatedAtUtc,
                    PrintedCount = counts.FirstOrDefault(x => x.Status == RunStatus.Printed)?.Count ?? 0,
                    SentCount = counts.FirstOrDefault(x => x.Status == RunStatus.Sent)?.Count ?? 0,
                    FailedCount = counts.FirstOrDefault(x => x.Status == RunStatus.Failed)?.Count ?? 0,
                    NoEmailCount = counts.FirstOrDefault(x => x.Status == RunStatus.NoEmail)?.Count ?? 0
                };
            }).ToList();

            return new Step3SendEmailVm
            {
                WorkflowKey = workflowKey,
                SettingsId = s.Id,
                RollId = s.RollId,
                RollShortCode = roll?.ShortCode ?? "",
                RollName = roll?.Name ?? s.RollName ?? "",
                Notice = s.Notice,
                VersionText = $"V{s.Version}",
                LetterDate = s.LetterDate,
                FinancialYearsText = s.FinancialYearsText,
                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                SignaturePath = s.SignaturePath,

                TotalBatches = batches.Count,
                TotalPrinted = rows.Sum(r => r.PrintedCount),
                TotalSent = rows.Sum(r => r.SentCount),
                MaxEmailsPerSend = 2000,
                Batches = rows,

                IsS52 = s.Notice == NoticeKind.S52,
                S52IsReview = s.IsSection52Review
            };
        }

        private async Task<Step3PrintVm> BuildTpaPrintVmAsync(
            NoticeSettings s,
            TpaRollSummary rollSummary,
            Guid workflowKey,
            CancellationToken ct)
        {
            var totalRecords = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .CountAsync(x => x.NoticeSettingsId == s.Id, ct);

            var totalPendingPrint = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .CountAsync(x =>
                    x.NoticeSettingsId == s.Id &&
                    (x.Status == null ||
                     x.Status == "" ||
                     x.Status == "Pending" ||
                     x.Status == "Print-Failed"),
                    ct);

            var totalPrinted = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .CountAsync(x =>
                    x.NoticeSettingsId == s.Id &&
                    (x.Status == "Printed" || x.Status == "Sent") &&
                    x.PdfPath != null &&
                    x.PdfPath != "",
                    ct);

            var totalFailed = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .CountAsync(x =>
                    x.NoticeSettingsId == s.Id &&
                    x.Status == "Print-Failed",
                    ct);

            return new Step3PrintVm
            {
                WorkflowKey = workflowKey,
                SettingsId = s.Id,
                RollId = rollSummary.RollId,
                RollShortCode = rollSummary.ShortCode,
                RollName = rollSummary.Name,

                Notice = s.Notice,
                VersionText = $"V{s.Version}",
                LetterDate = s.LetterDate,
                FinancialYearsText = s.FinancialYearsText,
                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                SignaturePath = s.SignaturePath,

                TotalBatches = 0,
                TotalRecordsBatched = totalRecords,
                TotalPrinted = totalPrinted,
                TotalFailed = totalFailed,
                Batches = new(),

                IsS52 = false,
                S52IsReview = false,
                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate
            };
        }

        private async Task<Step3SendEmailVm> BuildTpaEmailVmAsync(
            NoticeSettings s,
            TpaRollSummary rollSummary,
            Guid workflowKey,
            CancellationToken ct)
        {
            var totalReady = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .CountAsync(x =>
                    x.NoticeSettingsId == s.Id &&
                    (x.Status == "Printed" || x.Status == "Email-Failed") &&
                    x.PdfPath != null &&
                    x.PdfPath != "",
                    ct);

            var totalSent = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .CountAsync(x =>
                    x.NoticeSettingsId == s.Id &&
                    x.Status == "Sent",
                    ct);

            return new Step3SendEmailVm
            {
                WorkflowKey = workflowKey,
                SettingsId = s.Id,
                RollId = rollSummary.RollId,
                RollShortCode = rollSummary.ShortCode,
                RollName = rollSummary.Name,

                Notice = s.Notice,
                VersionText = $"V{s.Version}",
                LetterDate = s.LetterDate,
                FinancialYearsText = s.FinancialYearsText,
                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                SignaturePath = s.SignaturePath,

                TotalBatches = 0,
                TotalPrinted = totalReady,
                TotalSent = totalSent,
                MaxEmailsPerSend = 999999,
                Batches = new(),

                IsS52 = false,
                S52IsReview = false
            };
        }



        private async Task<Step3PrintVm> BuildClaPrintVmAsync(
            NoticeSettings settings,
            TpaRollSummary rollSummary,
            Guid workflowKey,
            CancellationToken ct)
        {
            var query = BuildClaNoticeQuery(settings);

            var totalRecords = await query.CountAsync(ct);

            var totalPrinted = await query.CountAsync(
                x =>
                    (x.Status == "Printed" ||
                     x.Status == "Sent" ||
                     x.Status == "Email-Failed") &&
                    x.PdfPath != null &&
                    x.PdfPath != "",
                ct);

            var totalFailed = await query.CountAsync(
                x => x.Status == "Print-Failed",
                ct);

            return new Step3PrintVm
            {
                WorkflowKey = workflowKey,
                SettingsId = settings.Id,

                RollId = rollSummary.RollId,
                RollShortCode = rollSummary.ShortCode,
                RollName = rollSummary.Name,

                Notice = settings.Notice,
                VersionText = $"V{settings.Version}",

                LetterDate = settings.LetterDate,
                FinancialYearsText = settings.FinancialYearsText,
                ObjectionStartDate = settings.ObjectionStartDate,
                ObjectionEndDate = settings.ObjectionEndDate,
                ExtensionDate = settings.ExtensionDate,
                SignaturePath = settings.SignaturePath,

                TotalBatches = 0,
                TotalRecordsBatched = totalRecords,
                TotalPrinted = totalPrinted,
                TotalFailed = totalFailed,
                Batches = new(),

                IsS52 = false,
                S52IsReview = false,
                BulkFromDate = settings.BulkFromDate,
                BulkToDate = settings.BulkToDate
            };
        }

        private async Task<Step3SendEmailVm> BuildClaEmailVmAsync(
            NoticeSettings settings,
            TpaRollSummary rollSummary,
            Guid workflowKey,
            CancellationToken ct)
        {
            var query = BuildClaNoticeQuery(settings);

            var totalReady = await query.CountAsync(
                x =>
                    (x.Status == "Printed" ||
                     x.Status == "Email-Failed") &&
                    x.PdfPath != null &&
                    x.PdfPath != "",
                ct);

            var totalSent = await query.CountAsync(
                x => x.Status == "Sent",
                ct);

            return new Step3SendEmailVm
            {
                WorkflowKey = workflowKey,
                SettingsId = settings.Id,

                RollId = rollSummary.RollId,
                RollShortCode = rollSummary.ShortCode,
                RollName = rollSummary.Name,

                Notice = settings.Notice,
                VersionText = $"V{settings.Version}",

                LetterDate = settings.LetterDate,
                FinancialYearsText = settings.FinancialYearsText,
                ObjectionStartDate = settings.ObjectionStartDate,
                ObjectionEndDate = settings.ObjectionEndDate,
                ExtensionDate = settings.ExtensionDate,
                SignaturePath = settings.SignaturePath,

                TotalBatches = 0,
                TotalPrinted = totalReady,
                TotalSent = totalSent,
                MaxEmailsPerSend = 999999,
                Batches = new(),

                IsS52 = false,
                S52IsReview = false
            };
        }

        private IQueryable<ClaThirdPartyApplicationNotice> BuildClaNoticeQuery(
            NoticeSettings settings)
        {
            var query = _db.ClaThirdPartyApplicationNotices
                .AsNoTracking()
                .Where(x => x.IsActive);

            var hasLinkedRows = _db.ClaThirdPartyApplicationNotices
                .AsNoTracking()
                .Any(x =>
                    x.IsActive &&
                    x.NoticeSettingsId == settings.Id);

            return hasLinkedRows
                ? query.Where(x => x.NoticeSettingsId == settings.Id)
                : query.Where(x =>
                    x.NoticeSettingsId == null ||
                    x.NoticeSettingsId == 0);
        }

        private async Task<TpaRollSummary> SynchronizeClaRollMetadataAsync(
            NoticeSettings settings,
            CancellationToken ct)
        {
            var rows = await _db.ClaThirdPartyApplicationNotices
                .Where(x =>
                    x.IsActive &&
                    (x.NoticeSettingsId == settings.Id ||
                     x.NoticeSettingsId == null ||
                     x.NoticeSettingsId == 0))
                .ToListAsync(ct);

            if (rows.Count == 0)
            {
                return new TpaRollSummary
                {
                    RollId = settings.RollId,
                    ShortCode = FirstNonEmpty(
                        settings.Roll.ToString(),
                        "GV23"),
                    Name = FirstNonEmpty(
                        settings.RollName,
                        settings.ValuationPeriodCode,
                        "CLA Third-Party Applications")
                };
            }

            var rolls = await _db.RollRegistry
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(ct);

            var changed = false;
            var resolvedRolls = new List<Domain.Rolls.RollRegistry>();

            foreach (var row in rows)
            {
                var rowChanged = false;

                var resolvedRoll = ResolveClaRoll(
                    row,
                    settings,
                    rolls);

                if (resolvedRoll is not null)
                {
                    resolvedRolls.Add(resolvedRoll);

                    if (row.RollId != resolvedRoll.RollId ||
                        !string.Equals(
                            row.RollShortCode,
                            resolvedRoll.ShortCode,
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(
                            row.ValuationPeriod,
                            resolvedRoll.Name,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        row.RollId = resolvedRoll.RollId;
                        row.RollShortCode = resolvedRoll.ShortCode;
                        row.ValuationPeriod = resolvedRoll.Name;
                        rowChanged = true;
                    }
                }

                if (row.NoticeSettingsId != settings.Id)
                {
                    row.NoticeSettingsId = settings.Id;
                    rowChanged = true;
                }

                var representationCloseDate =
                    settings.ObjectionEndDate ??
                    settings.LetterDate.AddDays(30);

                if (row.LetterDate != settings.LetterDate.Date)
                {
                    row.LetterDate = settings.LetterDate.Date;
                    rowChanged = true;
                }

                if (row.RepresentationCloseDate != representationCloseDate.Date)
                {
                    row.RepresentationCloseDate =
                        representationCloseDate.Date;

                    rowChanged = true;
                }

                if (rowChanged)
                {
                    row.UpdatedAtUtc = DateTime.UtcNow;
                    row.UpdatedBy = "SYSTEM-CLA-ROLL-SYNC";
                    changed = true;
                }
            }

            if (changed)
            {
                await _db.SaveChangesAsync(ct);
            }

            var distinctRolls = resolvedRolls
                .GroupBy(x => x.RollId)
                .Select(x => x.First())
                .OrderBy(x => x.RollId)
                .ToList();

            if (distinctRolls.Count == 1)
            {
                var single = distinctRolls[0];

                return new TpaRollSummary
                {
                    RollId = single.RollId,
                    ShortCode = single.ShortCode ?? "",
                    Name = single.Name ?? ""
                };
            }

            if (distinctRolls.Count > 1)
            {
                return new TpaRollSummary
                {
                    RollId = settings.RollId,
                    ShortCode = "MULTI",
                    Name = "Multiple Valuation Rolls"
                };
            }

            return new TpaRollSummary
            {
                RollId = settings.RollId,
                ShortCode = FirstNonEmpty(
                    rows.Select(x => x.RollShortCode)
                        .FirstOrDefault(x =>
                            !string.IsNullOrWhiteSpace(x)),
                    settings.Roll.ToString(),
                    "GV23"),

                Name = FirstNonEmpty(
                    rows.Select(x => x.ValuationPeriod)
                        .FirstOrDefault(x =>
                            !string.IsNullOrWhiteSpace(x)),
                    settings.RollName,
                    settings.ValuationPeriodCode,
                    "CLA Third-Party Applications")
            };
        }

        private static Domain.Rolls.RollRegistry? ResolveClaRoll(
            ClaThirdPartyApplicationNotice row,
            NoticeSettings settings,
            IReadOnlyCollection<Domain.Rolls.RollRegistry> activeRolls)
        {
            if (row.RollId.HasValue)
            {
                var byId = activeRolls.FirstOrDefault(
                    x => x.RollId == row.RollId.Value);

                if (byId is not null)
                    return byId;
            }

            var expectedShortCode = FirstNonEmpty(
                row.RollShortCode,
                settings.Roll.ToString(),
                settings.ValuationPeriodCode);

            if (string.IsNullOrWhiteSpace(expectedShortCode))
                return null;

            return activeRolls.FirstOrDefault(x =>
                NormalizeRollCode(x.ShortCode) ==
                NormalizeRollCode(expectedShortCode));
        }

        private static string FirstNonEmpty(
            params string?[] values)
        {
            return values
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x))
                ?.Trim()
                ?? "";
        }

        private async Task<TpaRollSummary> SynchronizeTpaRollMetadataAsync(
            NoticeSettings settings,
            CancellationToken ct)
        {
            var rows = await _db.ThirdPartyAppealApplicationNotices
                .Where(x =>
                    x.NoticeSettingsId == settings.Id &&
                    !string.IsNullOrWhiteSpace(x.Appeal_No))
                .ToListAsync(ct);

            if (rows.Count == 0)
            {
                return new TpaRollSummary
                {
                    RollId = settings.RollId,
                    ShortCode = "TPA",
                    Name = settings.RollName ?? "Third-Party Appeal Applications"
                };
            }

            var rolls = await _db.RollRegistry
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(ct);

            var changed = false;
            var resolvedRolls = new List<Domain.Rolls.RollRegistry>();

            foreach (var row in rows)
            {
                var shortCode = ResolveTpaShortCode(row.Appeal_No);

                var resolvedRoll = rolls.FirstOrDefault(x =>
                    NormalizeRollCode(x.ShortCode) ==
                    NormalizeRollCode(shortCode));

                if (resolvedRoll == null)
                {
                    throw new InvalidOperationException(
                        $"No active RollRegistry record was found for Appeal Number '{row.Appeal_No}' and ShortCode '{shortCode}'.");
                }

                resolvedRolls.Add(resolvedRoll);

                if (row.RollId != resolvedRoll.RollId ||
                    !string.Equals(
                        row.RollShortCode,
                        resolvedRoll.ShortCode,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        row.ValuationPeriod,
                        resolvedRoll.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    row.RollId = resolvedRoll.RollId;
                    row.RollShortCode = resolvedRoll.ShortCode;
                    row.ValuationPeriod = resolvedRoll.Name;
                    row.UpdatedAt = DateTime.UtcNow;
                    row.UpdatedBy = "SYSTEM-TPA-ROLL-SYNC";

                    changed = true;
                }
            }

            if (changed)
            {
                await _db.SaveChangesAsync(ct);
            }

            var distinctRolls = resolvedRolls
                .GroupBy(x => x.RollId)
                .Select(x => x.First())
                .OrderBy(x => x.RollId)
                .ToList();

            if (distinctRolls.Count == 1)
            {
                var single = distinctRolls[0];

                return new TpaRollSummary
                {
                    RollId = single.RollId,
                    ShortCode = single.ShortCode ?? "",
                    Name = single.Name ?? ""
                };
            }

            return new TpaRollSummary
            {
                RollId = settings.RollId,
                ShortCode = "MULTI",
                Name = "Multiple Valuation Rolls"
            };
        }

        private static string ResolveTpaShortCode(string? appealNo)
        {
            if (string.IsNullOrWhiteSpace(appealNo))
            {
                throw new InvalidOperationException(
                    "Appeal number is required to resolve the TPA roll.");
            }

            var value = appealNo.Trim();

            if (value.StartsWith(
                "APP-GV23-Sup3-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 3";
            }

            if (value.StartsWith(
                "APP-GV23-Sup2-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 2";
            }

            if (value.StartsWith(
                "APP-GV23-Sup1-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 1";
            }

            if (value.StartsWith(
                "APP-GV23-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "GV23";
            }

            throw new InvalidOperationException(
                $"Appeal Number '{appealNo}' does not match a supported TPA roll pattern.");
        }

        private static string NormalizeRollCode(string? value)
        {
            return (value ?? "")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();
        }

        private sealed class TpaRollSummary
        {
            public int RollId { get; set; }
            public string ShortCode { get; set; } = "";
            public string Name { get; set; } = "";
        }

        private async Task<(NoticeSettings s, Domain.Rolls.RollRegistry? roll)> ResolveAsync(
            Guid workflowKey,
            CancellationToken ct)
        {
            var s = await _db.NoticeSettings.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey, ct)
                    ?? await _db.NoticeSettings.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey, ct)
                    ?? throw new InvalidOperationException("Workflow not found.");

            var roll = await _db.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);

            if (roll == null &&
                s.Notice != NoticeKind.TPA &&
                s.Notice != NoticeKind.CLA_TPA)
            {
                throw new InvalidOperationException("Roll not found.");
            }

            return (s, roll);
        }
    }
}
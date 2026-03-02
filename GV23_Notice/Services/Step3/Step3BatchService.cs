using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Step3
{
    public sealed class Step3BatchService : IStep3BatchService
    {
        private readonly AppDbContext _db;

        public Step3BatchService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<int> CreateBatchAsync(Guid workflowKey, DateTime batchDate, string createdBy, CancellationToken ct)
        {
            // 1) Resolve settings
            var s = await _db.NoticeSettings
                        .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey, ct)
                    ?? await _db.NoticeSettings
                        .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey, ct)
                    ?? throw new InvalidOperationException("Workflow not found.");

            if (!s.IsApproved)
                throw new InvalidOperationException("Step 1 must be approved before creating Step 3 batch.");

            if (!s.Step2Approved && !s.Step2CorrectionRequested)
                throw new InvalidOperationException("Step 2 must be approved or correction-requested before batch creation.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                       ?? throw new InvalidOperationException("Roll not found.");

            // 2) Compute correct prefix per notice type
            var shortCode = roll.ShortCode ?? "";
            var prefix = s.Notice switch
            {
                NoticeKind.S52 => (s.IsSection52Review ? "S52" : "AD") + $"_{shortCode}_",
                NoticeKind.DJ => $"DJ_{shortCode}_",
                NoticeKind.IN => ((s.IsInvalidOmission ?? false) ? "IOM" : "IOBJ") + $"_{shortCode}_",
                _ => $"{s.Notice}_{shortCode}_"
            };

            // 3) Compute next sequence using the correct prefix
            var last = await _db.NoticeBatches.AsNoTracking()
                .Where(b => b.RollId == s.RollId
                         && b.BatchKind == "STEP3"
                         && b.BatchName.StartsWith(prefix))
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync(ct);

            var nextSeq = 1;
            if (last != null && last.BatchName.Length > prefix.Length)
            {
                var suf = last.BatchName[prefix.Length..];
                if (int.TryParse(suf, out var parsed))
                    nextSeq = parsed + 1;
            }

            var batchName = $"{prefix}{nextSeq:0000}";
            var batchDateUtc = batchDate.Date;
            var nowUtc = DateTime.UtcNow;

            // 4) Insert batch header
            var batch = new NoticeBatch
            {
                NoticeSettingsId = s.Id,
                WorkflowKey = workflowKey,
                RollId = s.RollId,
                Mode = s.Mode,
                Notice = s.Notice,
                SettingsVersionUsed = s.Version,
                Version = s.Version.ToString(),
                BatchKind = "STEP3",
                BatchName = batchName,
                BatchDate = batchDateUtc,   // ✅ always set
                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate,
                NumberOfRecords = 0,
                IsApproved = false,
                CreatedBy = createdBy,
                CreatedAtUtc = nowUtc
            };

            _db.NoticeBatches.Add(batch);
            await _db.SaveChangesAsync(ct);

            // 5) Notice-specific SP call — all now pass BatchName + BatchDate
            switch (s.Notice)
            {
                case NoticeKind.S49:
                    {
                        // SP: EXEC dbo.S49_Step3_AssignTop500ToBatch @RollId, @BatchName, @BatchDate
                        var picked = await _db.Set<S49BatchPickRow>()
                            .FromSqlRaw("EXEC dbo.S49_Step3_AssignTop500ToBatch @p0, @p1, @p2",
                                s.RollId, batchName, batchDateUtc)
                            .ToListAsync(ct);

                        var runLogs = picked
                            .Where(x => !string.IsNullOrWhiteSpace(x.PremiseId))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            }).ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                case NoticeKind.S51:
                    {
                        var closingDate = (s.EvidenceCloseDate ?? s.LetterDate.AddDays(30)).Date;

                        // SP: EXEC dbo.S51_Step3_InsertTop500IntoSection51Table @RollId, @BatchName, @BatchDate, @ClosingDate
                        var picked = await _db.Set<S51BatchPickRow>()
                            .FromSqlRaw("EXEC dbo.S51_Step3_InsertTop500IntoSection51Table @p0, @p1, @p2, @p3",
                                s.RollId, batchName, batchDateUtc, closingDate)
                            .ToListAsync(ct);

                        var runLogs = picked
                            .Where(x => !string.IsNullOrWhiteSpace(x.ObjectionNo))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                ObjectionNo = x.ObjectionNo,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            }).ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                case NoticeKind.S52:
                    {
                        if (!s.BulkFromDate.HasValue || !s.BulkToDate.HasValue)
                            throw new InvalidOperationException("S52 batch requires Bulk From and Bulk To dates.");

                        // SP: EXEC dbo.S52_Step3_SelectTop500ByRange @RollId, @FromDate, @ToDate, @IsReview
                        var picked = await _db.Set<S52BatchPickRow>()
                            .FromSqlRaw("EXEC dbo.S52_Step3_SelectTop500ByRange @p0, @p1, @p2, @p3",
                                s.RollId,
                                s.BulkFromDate.Value.Date,
                                s.BulkToDate.Value.Date,
                                s.IsSection52Review)
                            .ToListAsync(ct);

                        var runLogs = picked
                            .Where(x => !string.IsNullOrWhiteSpace(x.AppealNo))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                AppealNo = x.AppealNo,
                                ObjectionNo = x.ObjectionNo,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            }).ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                case NoticeKind.DJ:
                    {
                        // SP: EXEC dbo.DJ_Step3_InsertTop500IntoDearJohnnyTable @RollId, @BatchName, @BatchDate
                        var picked = await _db.Set<DjBatchPickRow>()
                            .FromSqlRaw("EXEC dbo.DJ_Step3_InsertTop500IntoDearJohnnyTable @p0, @p1, @p2",
                                s.RollId, batchName, batchDateUtc)
                            .ToListAsync(ct);

                        var runLogs = picked
                            .Where(x => !string.IsNullOrWhiteSpace(x.ObjectionNo))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                ObjectionNo = x.ObjectionNo,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            }).ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                case NoticeKind.IN:
                    {
                        var isOmission = s.IsInvalidOmission ?? false;

                        // SP: EXEC dbo.IN_Step3_InsertTop500IntoInvalidNoticeTable @RollId, @IsOmission, @BatchName, @BatchDate
                        var picked = await _db.Set<InBatchPickRow>()
                            .FromSqlRaw("EXEC dbo.IN_Step3_InsertTop500IntoInvalidNoticeTable @p0, @p1, @p2, @p3",
                                s.RollId, isOmission, batchName, batchDateUtc)
                            .ToListAsync(ct);

                        var runLogs = picked
                            .Where(x => !string.IsNullOrWhiteSpace(x.ObjectionNo))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                ObjectionNo = x.ObjectionNo,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            }).ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                default:
                    // Remove the incomplete batch header since nothing was assigned
                    _db.NoticeBatches.Remove(batch);
                    await _db.SaveChangesAsync(ct);
                    throw new NotSupportedException($"Step3 batch creation not implemented for notice: {s.Notice}");
            }
        }

        // ── Legacy overload (kept for backward compat) ───────────────────────
        public async Task<NoticeBatch> CreateBatchAsync(int settingsId, string createdBy, CancellationToken ct)
        {
            var s = await _db.NoticeSettings.FirstOrDefaultAsync(x => x.Id == settingsId, ct)
                    ?? throw new InvalidOperationException("NoticeSettings not found.");

            if (!s.IsApproved)
                throw new InvalidOperationException("Settings not approved.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                       ?? throw new InvalidOperationException("RollRegistry not found.");

            var prefix = $"{s.Notice}_{roll.ShortCode}_";
            var last = await _db.NoticeBatches.AsNoTracking()
                .Where(x => x.NoticeSettingsId == settingsId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var nextNo = 1;
            if (last != null && last.BatchName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var tail = last.BatchName[prefix.Length..];
                if (int.TryParse(tail, out var parsed)) nextNo = parsed + 1;
            }

            var batchName = $"{prefix}{nextNo:0000}";
            var batchDate = DateTime.Today;

            var batch = new NoticeBatch
            {
                NoticeSettingsId = s.Id,
                BatchName = batchName,
                BatchDate = batchDate,
                CreatedBy = createdBy,
                CreatedAtUtc = DateTime.UtcNow,
                Roll = Enum.TryParse<RollCode>(roll.ShortCode, out var rc) ? rc : RollCode.GV23,
                Notice = s.Notice,
                SettingsVersionUsed = s.Version,
                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate,
                BatchKind = $"{s.Notice} {s.Mode}",
                WorkflowKey = s.ApprovalKey ?? Guid.NewGuid(),
                RollId = s.RollId,
                Mode = s.Mode,
                Version = $"v{s.Version}",
                NumberOfRecords = 0,
                IsApproved = true,
                ApprovedBy = s.ApprovedBy,
                ApprovedAtUtc = s.ApprovedAtUtc
            };

            _db.NoticeBatches.Add(batch);
            await _db.SaveChangesAsync(ct);

            if (s.Notice == NoticeKind.S49)
            {
                var res = await _db.Set<S49AssignBatchResultDto>()
                    .FromSqlRaw("EXEC dbo.S49_Step3_AssignTop500PremisesToBatch @p0, @p1, @p2",
                        s.RollId, batchName, batchDate.Date)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                batch.NumberOfRecords = res?.PremiseCount ?? 0;
                await _db.SaveChangesAsync(ct);
            }

            return batch;
        }
    }
}

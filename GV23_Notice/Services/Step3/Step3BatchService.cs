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
            // 1) Resolve settings by key
            var s = await _db.NoticeSettings
                .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey, ct)
                ?? throw new InvalidOperationException("Workflow not found.");

            if (!s.IsApproved)
                throw new InvalidOperationException("Step 1 must be approved before creating Step 3 batch.");

            // Step2 must be approved OR correction requested (still trackable)
            if (!s.Step2Approved && !s.Step2CorrectionRequested)
                throw new InvalidOperationException("Step 2 must be approved or correction-requested before batch creation.");

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                ?? throw new InvalidOperationException("Roll not found.");

            // 2) Build next batch name: S49_{RollShortCode}_0001 / S51_{RollShortCode}_0001
            var prefix = $"{s.Notice}_{roll.ShortCode}_";   // ex: S51_GV23-Sup2-8_
            var last = await _db.NoticeBatches
                .AsNoTracking()
                .Where(b => b.RollId == s.RollId && b.Notice == s.Notice && b.BatchKind == "STEP3")
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync(ct);

            var nextSeq = 1;
            if (last != null && !string.IsNullOrWhiteSpace(last.BatchName) &&
                last.BatchName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var suf = last.BatchName.Substring(prefix.Length);
                if (int.TryParse(suf, out var parsed))
                    nextSeq = parsed + 1;
            }

            var batchName = $"{prefix}{nextSeq:0000}";

            // Version safe
            var versionText = s.Version.ToString();

            // 3) Insert batch header now (NumberOfRecords updated after picks)
            var batch = new NoticeBatch
            {
                WorkflowKey = workflowKey,
                RollId = s.RollId,
                Notice = s.Notice,
                Version = versionText,
                BatchKind = "STEP3",
                BatchName = batchName,
                BatchDate = batchDate.Date,
                CreatedBy = createdBy,
                CreatedAtUtc = DateTime.UtcNow,
                NumberOfRecords = 0,
                IsApproved = false
            };

            _db.NoticeBatches.Add(batch);
            await _db.SaveChangesAsync(ct);

            // 4) Notice-specific pick + roll/app table updates (Top 500)
            var nowUtc = DateTime.UtcNow;

            if (s.Notice == NoticeKind.S49)
            {
                // Assign Top 500 in roll DB + update Batch_Name there
                var picked = await _db.Set<S49BatchPickRow>()
                    .FromSqlRaw("EXEC dbo.S49_Step3_AssignTop500ToBatch @p0, @p1", s.RollId, batchName)
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
                    })
                    .ToList();

                if (runLogs.Count > 0)
                    _db.NoticeRunLogs.AddRange(runLogs);

                batch.NumberOfRecords = runLogs.Count;
                await _db.SaveChangesAsync(ct);

                return batch.Id;
            }

            if (s.Notice == NoticeKind.S51)
            {
                // Closing date used inside Section51Table insert (column [Closing Date])
                // Choose the correct source: EvidenceCloseDate is typical for S51.
                var closingDate = (s.EvidenceCloseDate ?? s.LetterDate.AddDays(30)).Date;

                // Insert Top 500 into APP DB dbo.Section51Table (proc reads from roll DB using Roll_Resolve + join sapContact)
                var picked = await _db.Set<S51BatchPickRow>()
                    .FromSqlRaw(
                        "EXEC dbo.S51_Step3_InsertTop500IntoSection51Table @p0, @p1, @p2, @p3",
                        s.RollId,
                        batchName,
                        batchDate.Date,
                        closingDate)
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
                    })
                    .ToList();

                if (runLogs.Count > 0)
                    _db.NoticeRunLogs.AddRange(runLogs);

                batch.NumberOfRecords = runLogs.Count;
                await _db.SaveChangesAsync(ct);

                return batch.Id;
            }
            if (s.Notice == NoticeKind.S52)
            {
                if (!s.BulkFromDate.HasValue || !s.BulkToDate.HasValue)
                    throw new InvalidOperationException("Section 52 batch requires Bulk From and Bulk To dates.");

                // YOU MUST have a persisted flag for S52 type:
                // true => Review (System_Generated), false => Appeal Decision
                var isReview = s.IsSection52Review; // <-- rename to your real property

                // IMPORTANT: batch name prefix rule
                // S52_{Roll}_0001 for review
                // AD_{Roll}_0001 for appeal decision
                var prefixOverride = (isReview ? "S52" : "AD");
                var s52prefix = $"{prefixOverride}_{roll.ShortCode}_";

                // regenerate sequence using the override prefix (not s.Notice)
                var lasts = await _db.NoticeBatches
                    .AsNoTracking()
                    .Where(b => b.RollId == s.RollId && b.BatchKind == "STEP3" && b.BatchName.StartsWith(prefix))
                    .OrderByDescending(b => b.Id)
                    .FirstOrDefaultAsync(ct);

                var S52nextSeq = 1;
                if (last != null && !string.IsNullOrWhiteSpace(last.BatchName) &&
                    last.BatchName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suf = last.BatchName.Substring(prefix.Length);
                    if (int.TryParse(suf, out var parsed)) nextSeq = parsed + 1;
                }

                // override batch name on the existing batch header
                batch.BatchName = $"{prefix}{nextSeq:0000}";

                // store range as backup in NoticeBatch fields (recommended):
                // If you already have fields, set them. If not, ignore.
                // batch.RangeFrom = s.BulkFromDate.Value.Date; etc...

                await _db.SaveChangesAsync(ct);

                var picked = await _db.Set<S52BatchPickRow>()
                    .FromSqlRaw(
                        "EXEC dbo.S52_Step3_SelectTop500ByRange @p0, @p1, @p2, @p3",
                        s.RollId,
                        s.BulkFromDate.Value.Date,
                        s.BulkToDate.Value.Date,
                        isReview)
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
                    })
                    .ToList();

                if (runLogs.Count > 0)
                    _db.NoticeRunLogs.AddRange(runLogs);

                batch.NumberOfRecords = runLogs.Count;
                await _db.SaveChangesAsync(ct);

                return batch.Id;
            }

            if (s.Notice == NoticeKind.DJ)
            {
                // Override prefix to DJ_
                var djPrefix = $"DJ_{roll.ShortCode}_";

                var lastDj = await _db.NoticeBatches.AsNoTracking()
                    .Where(b => b.RollId == s.RollId && b.BatchKind == "STEP3" && b.BatchName.StartsWith(djPrefix))
                    .OrderByDescending(b => b.Id)
                    .FirstOrDefaultAsync(ct);

               
                if (lastDj != null && !string.IsNullOrWhiteSpace(lastDj.BatchName) &&
                    lastDj.BatchName.StartsWith(djPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suf = lastDj.BatchName.Substring(djPrefix.Length);
                    if (int.TryParse(suf, out var parsed)) nextSeq = parsed + 1;
                }

                batch.BatchName = $"{djPrefix}{nextSeq:0000}";
                await _db.SaveChangesAsync(ct);

                var picked = await _db.Set<DjBatchPickRow>()
                    .FromSqlRaw("EXEC dbo.DJ_Step3_InsertTop500IntoDearJohnnyTable @p0, @p1, @p2",
                        s.RollId, batch.BatchName, batchDate.Date)
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
                    })
                    .ToList();

                if (runLogs.Count > 0)
                    _db.NoticeRunLogs.AddRange(runLogs);

                batch.NumberOfRecords = runLogs.Count;
                await _db.SaveChangesAsync(ct);

                return batch.Id;
            }

            if (s.Notice == NoticeKind.IN)
            {
                // Need persisted invalid kind
                var isOmission = s.IsInvalidOmission ?? false; // true => omission, false => objection
                var prefixOverride = isOmission ? "IOM" : "IOBJ";
                var inPrefix = $"{prefixOverride}_{roll.ShortCode}_";

                var lastIn = await _db.NoticeBatches.AsNoTracking()
                    .Where(b => b.RollId == s.RollId && b.BatchKind == "STEP3" && b.BatchName.StartsWith(inPrefix))
                    .OrderByDescending(b => b.Id)
                    .FirstOrDefaultAsync(ct);

                var DJnextSeq = 1;
                if (lastIn != null && !string.IsNullOrWhiteSpace(lastIn.BatchName) &&
                    lastIn.BatchName.StartsWith(inPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suf = lastIn.BatchName.Substring(inPrefix.Length);
                    if (int.TryParse(suf, out var parsed)) nextSeq = parsed + 1;
                }

                batch.BatchName = $"{inPrefix}{nextSeq:0000}";
                await _db.SaveChangesAsync(ct);

                var picked = await _db.Set<InBatchPickRow>()
                    .FromSqlRaw("EXEC dbo.IN_Step3_InsertTop500IntoInvalidNoticeTable @p0, @p1, @p2, @p3",
                        s.RollId, isOmission, batch.BatchName, batchDate.Date)
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
                    })
                    .ToList();

                if (runLogs.Count > 0)
                    _db.NoticeRunLogs.AddRange(runLogs);

                batch.NumberOfRecords = runLogs.Count;
                await _db.SaveChangesAsync(ct);

                return batch.Id;
            }
            // If we reach here, the notice isn't implemented yet
            throw new NotSupportedException($"Step3 batch creation not implemented for notice: {s.Notice}");
        }
    }
}

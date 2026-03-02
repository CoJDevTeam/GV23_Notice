using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.DTOs;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Step3
{
    public sealed class Step3BatchQueryService : IStep3BatchQueryService
    {
        private readonly AppDbContext _db;

        public Step3BatchQueryService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Step3Step2Vm> BuildAsync(Guid workflowKey, CancellationToken ct)
        {
            // ── 1) Resolve settings ──────────────────────────────────────────
            var s = await _db.NoticeSettings
                          .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey, ct)
                    ?? await _db.NoticeSettings
                          .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey, ct)
                    ?? throw new InvalidOperationException("Workflow not found.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                       ?? throw new InvalidOperationException("Roll not found.");

            var versionText = s.Version.ToString();

            // ── 2) Load batches for this workflow ────────────────────────────
            var batches = await _db.NoticeBatches
                .AsNoTracking()
                .Where(b => b.WorkflowKey == workflowKey
                         && b.RollId == s.RollId
                         && b.Notice == s.Notice
                         && b.BatchKind == "STEP3")
                .OrderByDescending(b => b.Id)
                .Take(200)
                .ToListAsync(ct);

            // ── 3) Counts from batches already in DB ─────────────────────────
            var createdBatchCount = batches.Count;
            var createdRecordCount = batches.Sum(b => b.NumberOfRecords);

            // ── 4) Pending count — per notice type ───────────────────────────
            int totalPending;
            int pendingInBatches;

            if (s.Notice == NoticeKind.S49)
            {
                // ✅ Materialise first (no SQL composition), then take first row in memory
                var rows = await _db.Set<S49PendingCountDto>()
                    .FromSqlRaw("EXEC dbo.S49_Step3_CountPending @p0", s.RollId)
                    .AsNoTracking()
                    .ToListAsync(ct);

                var countRow = rows.FirstOrDefault();

                // PendingPremiseCount = not yet assigned to any batch (Batch_Name IS NULL)
                var stillPending = countRow?.PendingPremiseCount ?? 0;

                // Total = already batched + still pending
                totalPending = createdRecordCount + stillPending;
                pendingInBatches = createdRecordCount;
            }
            else
            {
                totalPending = createdRecordCount;
                pendingInBatches = createdRecordCount;
            }

            var remaining = Math.Max(0, totalPending - pendingInBatches);

            // ── 5) Build VM ──────────────────────────────────────────────────
            return new Step3Step2Vm
            {
                WorkflowKey = workflowKey,
                RollId = s.RollId,
                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",
                Notice = s.Notice,
                VersionText = versionText,
                BatchDate = DateTime.Today,
                BatchSize = 500,

                TotalPendingRecords = totalPending,
                BatchesCreatedCount = createdBatchCount,
                TotalRecordsInCreatedBatches = pendingInBatches,

                LetterDate = s.LetterDate,
                FinancialYearsText = s.FinancialYearsText,
                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                SignaturePath = s.SignaturePath,

                Batches = batches.Select(b => new Step3BatchRowVm
                {
                    BatchId = b.Id,
                    BatchName = b.BatchName,
                    BatchDate = b.BatchDate,
                    NumberOfRecords = b.NumberOfRecords,
                    CreatedBy = b.CreatedBy,
                    CreatedAtUtc = b.CreatedAtUtc,
                    IsApproved = b.IsApproved,
                    ApprovedBy = b.ApprovedBy,
                    ApprovedAtUtc = b.ApprovedAtUtc
                }).ToList()
            };
        }
        public async Task<S49PendingCountDto> GetS49PendingAsync(int rollId, CancellationToken ct)
        {
            var row = await _db.Set<S49PendingCountDto>()
                .FromSqlRaw("EXEC dbo.S49_Step3_CountPending @p0", rollId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            return row ?? new S49PendingCountDto();
        }
    }
}
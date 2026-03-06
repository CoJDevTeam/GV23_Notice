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

            var batches = await _db.NoticeBatches.AsNoTracking()
                .Where(b => b.WorkflowKey == workflowKey
                         && b.RollId == s.RollId
                         && b.Notice == s.Notice
                         && b.BatchKind == "STEP3")
                .OrderBy(b => b.Id)
                .ToListAsync(ct);

            var batchIds = batches.Select(b => b.Id).ToList();

            // Load run log status counts per batch in one query
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
                    SentCount = counts.FirstOrDefault(x => x.Status == RunStatus.Sent)?.Count ?? 0,
                };
            }).ToList();

            return new Step3PrintVm
            {
                WorkflowKey = workflowKey,
                RollId = s.RollId,
                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",
                Notice = s.Notice,
                VersionText = s.Version.ToString(),
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
                Batches = rows
            };
        }

        public async Task<Step3SendEmailVm> BuildEmailVmAsync(Guid workflowKey, CancellationToken ct)
        {
            var (s, roll) = await ResolveAsync(workflowKey, ct);

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
                    NoEmailCount = counts.FirstOrDefault(x => x.Status == RunStatus.NoEmail)?.Count ?? 0,
                };
            }).ToList();

            return new Step3SendEmailVm
            {
                WorkflowKey = workflowKey,
                RollId = s.RollId,
                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",
                Notice = s.Notice,
                VersionText = s.Version.ToString(),
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
                Batches = rows
            };
        }

        // ── Shared resolver ─────────────────────────────────────────────────
        private async Task<(NoticeSettings s, Domain.Rolls.RollRegistry roll)> ResolveAsync(
            Guid workflowKey, CancellationToken ct)
        {
            var s = await _db.NoticeSettings.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey, ct)
                    ?? await _db.NoticeSettings.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey, ct)
                    ?? throw new InvalidOperationException("Workflow not found.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                       ?? throw new InvalidOperationException("Roll not found.");

            return (s, roll);
        }
    }
}




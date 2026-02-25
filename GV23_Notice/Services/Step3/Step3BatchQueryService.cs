using GV23_Notice.Data;
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
            var s = await _db.NoticeSettings
                  .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey, ct);

            // fallback (older flows might have WorkflowKey)
            if (s is null)
            {
                s = await _db.NoticeSettings
                    .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey, ct);
            }

            if (s is null)
                throw new InvalidOperationException("Workflow not found.");


            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                ?? throw new InvalidOperationException("Roll not found.");

            var version = s.Version.ToString();

            var batches = await _db.NoticeBatches
                .AsNoTracking()
                .Where(b => b.WorkflowKey == workflowKey
                         && b.RollId == s.RollId
                         && b.Notice == s.Notice
                         && b.Version == version
                         && b.BatchKind == "STEP3")
                .OrderByDescending(b => b.Id)
                .Take(200)
                .ToListAsync(ct);

            return new Step3Step2Vm
            {
                WorkflowKey = workflowKey,
                RollId = s.RollId,
                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",
                Notice = s.Notice,
                VersionText = version,
                BatchDate = DateTime.Today,
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

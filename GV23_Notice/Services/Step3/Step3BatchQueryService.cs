using GV23_Notice.Data;
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
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey, ct)
                ?? throw new InvalidOperationException("Workflow not found.");

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
    }
}

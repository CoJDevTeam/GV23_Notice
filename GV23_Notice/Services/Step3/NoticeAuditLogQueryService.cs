using GV23_Notice.Data;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Step3
{
    public sealed class NoticeAuditLogQueryService : INoticeAuditLogQueryService
    {
        private readonly AppDbContext _db;

        public NoticeAuditLogQueryService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<AuditLogRowVm>> GetStep2AuditLogsAsync(int rollId, string version, Guid workflowKey, CancellationToken ct)
        {
            var batch = await _db.NoticeBatches
                .AsNoTracking()
                .Where(b =>
                    b.BatchKind == "STEP2_AUDIT" &&
                    b.WorkflowKey == workflowKey &&
                    b.RollId == rollId &&
                    b.Version == version)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync(ct);

            if (batch is null) return new List<AuditLogRowVm>();

            var logs = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x => x.NoticeBatchId == batch.Id)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(200)
                .ToListAsync(ct);

            return logs.Select(x => new AuditLogRowVm
            {
                AtUtc = x.CreatedAtUtc,
                Status = x.Status.ToString(),
                To = x.RecipientEmail ?? "",
                PdfPath = x.PdfPath,
                EmlPath = x.EmlPath,
                Error = x.ErrorMessage
            }).ToList();
        }

        public async Task<(string? EmlPath, string? PdfPath, string? RecipientEmail, DateTime CreatedAtUtc)> GetLatestStep2AuditEmailAsync(
            int rollId, string version, Guid workflowKey, CancellationToken ct)
        {
            var batch = await _db.NoticeBatches
                .AsNoTracking()
                .Where(b =>
                    b.BatchKind == "STEP2_AUDIT" &&
                    b.WorkflowKey == workflowKey &&
                    b.RollId == rollId &&
                    b.Version == version)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync(ct);

            if (batch is null) return (null, null, null, default);

            var last = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x => x.NoticeBatchId == batch.Id)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (last is null) return (null, null, null, default);

            return (last.EmlPath, last.PdfPath, last.RecipientEmail, last.CreatedAtUtc);
        }
    }
}

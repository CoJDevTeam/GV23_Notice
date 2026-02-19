using GV23_Notice.Data;
using GV23_Notice.Models.DTOs;

namespace GV23_Notice.Services.Audit
{
    public interface INoticeSettingsAuditService
    {
        Task WriteAsync(int settingsId, string step, string action, string by, object? snapshot, string? comment, CancellationToken ct);
    }

    public sealed class NoticeSettingsAuditService : INoticeSettingsAuditService
    {
        private readonly AppDbContext _db;
        public NoticeSettingsAuditService(AppDbContext db) => _db = db;

        public async Task WriteAsync(int settingsId, string step, string action, string by, object? snapshot, string? comment, CancellationToken ct)
        {
            var json = snapshot is null ? null : System.Text.Json.JsonSerializer.Serialize(snapshot);

            _db.NoticeSettingsAudits.Add(new NoticeSettingsAudit
            {
                SettingsId = settingsId,
                Step = step,
                Action = action,
                PerformedBy = by,
                PerformedAtUtc = DateTime.UtcNow,
                Comment = comment,
                SnapshotJson = json
            });

            await _db.SaveChangesAsync(ct);
        }
    }

}

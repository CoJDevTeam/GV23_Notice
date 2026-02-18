using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services
{
    public interface IBatchNameService
    {
        Task<string> NextBatchNameAsync(RollCode roll, NoticeKind notice, DateTime date, CancellationToken ct);
    }

    public sealed class BatchNameService : IBatchNameService
    {
        private readonly AppDbContext _db;

        public BatchNameService(AppDbContext db) => _db = db;

        private static string Short(NoticeKind n) => n switch
        {
            NoticeKind.S49 => "S49",
            NoticeKind.S51 => "S51",
            NoticeKind.S52 => "S52",
            NoticeKind.S53 => "S53",
            NoticeKind.DJ => "DJ",
            NoticeKind.IN => "IN",
            NoticeKind.S78 => "S78",
            _ => n.ToString()
        };

        public async Task<string> NextBatchNameAsync(RollCode roll, NoticeKind notice, DateTime date, CancellationToken ct)
        {
            var ymd = date.ToString("yyyyMMdd");
            var prefix = $"{roll}_{Short(notice)}_{ymd}_";

            // Find existing matching batch names and take max suffix
            var existing = await _db.NoticeBatches
                .AsNoTracking()
                .Where(b => b.BatchName.StartsWith(prefix))
                .Select(b => b.BatchName)
                .ToListAsync(ct);

            var max = 0;
            foreach (var name in existing)
            {
                var tail = name.Substring(prefix.Length);
                if (int.TryParse(tail, out var n) && n > max) max = n;
            }

            var next = (max + 1).ToString("000");
            return prefix + next;
        }
    }
}

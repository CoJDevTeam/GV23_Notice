using GV23_Notice.Data;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Step3
{
    public sealed class CorrectionTicketQueryService : ICorrectionTicketQueryService
    {
        private readonly AppDbContext _db;

        public CorrectionTicketQueryService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<CorrectionTicketRowVm>> ListBySettingsAsync(int settingsId, CancellationToken ct)
        {
            var rows = await _db.CorrectionTickets
                .AsNoTracking()
                .Where(t => t.NoticeSettingsId == settingsId)
                .OrderByDescending(t => t.RequestedAtUtc)
                .ToListAsync(ct);

            return rows.Select(t => new CorrectionTicketRowVm
            {
                Id = t.Id,
                Status = t.Status,
                Title = t.Title,
                Description = t.Description,
                RequestedBy = t.RequestedBy,
                RequestedAtUtc = t.RequestedAtUtc,
                RequestComment = t.RequestComment,
                ResolvedBy = t.ResolvedBy,
                ResolvedAtUtc = t.ResolvedAtUtc
            }).ToList();
        }
    }
}

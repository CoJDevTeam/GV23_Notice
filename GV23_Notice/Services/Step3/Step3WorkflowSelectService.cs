using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Preview;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Step3
{
    public sealed class Step3WorkflowSelectService : IStep3WorkflowSelectService
    {
        private readonly AppDbContext _db;

        public Step3WorkflowSelectService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Step3SelectVm> BuildAsync(int? rollId, NoticeKind? notice, CancellationToken ct)
        {
            // Rolls dropdown (active rolls only)
            var rolls = await _db.RollRegistry
                .AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.ShortCode)
                .ToListAsync(ct);

            var rollItems = rolls
                .Select(r => new SelectListItem
                {
                    Value = r.RollId.ToString(),
                    Text = $"{r.ShortCode} - {r.Name}",
                    Selected = rollId.HasValue && r.RollId == rollId.Value
                })
                .ToList();

            // Notices dropdown
            var noticeItems = Enum.GetValues(typeof(NoticeKind))
                .Cast<NoticeKind>()
                .Select(n => new SelectListItem
                {
                    Value = n.ToString(),
                    Text = n.ToString(),
                    Selected = notice.HasValue && notice.Value == n
                })
                .ToList();

            // Results only when BOTH are selected (same behaviour as Step1)
            var rows = new List<Step3WorkflowRowVm>();

            if (rollId.HasValue && notice.HasValue)
            {
                var q = _db.NoticeSettings.AsNoTracking()
                    .Where(x => x.RollId == rollId.Value)
                    .Where(x => x.Notice == notice.Value)
                    .Where(x => x.IsApproved)
                    .Where(x => x.WorkflowKey != null && x.WorkflowKey != Guid.Empty)
                    .Where(x => x.Step2Approved || x.Step2CorrectionRequested);

                var settings = await q
                    .OrderByDescending(x => x.Step2ApprovedAt ?? x.Step2CorrectionRequestedAt ?? x.LetterDate)
                    .Take(200)
                    .ToListAsync(ct);

                var roll = rolls.FirstOrDefault(r => r.RollId == rollId.Value);

                rows = settings.Select(s => new Step3WorkflowRowVm
                {
                    WorkflowKey = s.WorkflowKey!.Value,
                    SettingsId = s.Id,
                    RollShortCode = roll?.ShortCode ?? "",
                    RollName = roll?.Name ?? "",
                    Notice = s.Notice,
                    Mode = (PreviewMode)s.Mode,
                    VersionText = s.Version.ToString(),
                    LetterDate = s.LetterDate,
                    Step2Approved = s.Step2Approved,
                    Step2CorrectionRequested = s.Step2CorrectionRequested
                }).ToList();
            }

            return new Step3SelectVm
            {
                RollId = rollId,
                Notice = notice,
                Rolls = rollItems,
                Notices = noticeItems,
                Rows = rows
            };
        }
    }
}


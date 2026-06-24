using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.Stats;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Stats
{
    public sealed class NoticeStatsDashboardService : INoticeStatsDashboardService
    {
        private readonly AppDbContext _db;

        public NoticeStatsDashboardService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<NoticeStatsDashboardVm> BuildDashboardAsync(
            NoticeStatsFilterVm filter,
            CancellationToken ct)
        {
            var batchesQuery =
                from b in _db.NoticeBatches.AsNoTracking()
                join s in _db.NoticeSettings.AsNoTracking()
                    on b.NoticeSettingsId equals s.Id
                join r in _db.RollRegistry.AsNoTracking()
                    on b.RollId equals r.RollId
                where b.BatchKind == "STEP3"
                select new
                {
                    Batch = b,
                    Settings = s,
                    Roll = r
                };

            if (filter.RollId.HasValue)
                batchesQuery = batchesQuery.Where(x => x.Batch.RollId == filter.RollId.Value);

            if (filter.Notice.HasValue)
                batchesQuery = batchesQuery.Where(x => x.Settings.Notice == filter.Notice.Value);

            if (!string.IsNullOrWhiteSpace(filter.BatchName))
            {
                var name = filter.BatchName.Trim();
                batchesQuery = batchesQuery.Where(x => x.Batch.BatchName.Contains(name));
            }

            if (filter.DateFrom.HasValue)
                batchesQuery = batchesQuery.Where(x => x.Batch.BatchDate >= filter.DateFrom.Value.Date);

            if (filter.DateTo.HasValue)
            {
                var to = filter.DateTo.Value.Date.AddDays(1);
                batchesQuery = batchesQuery.Where(x => x.Batch.BatchDate < to);
            }

            var batchData = await batchesQuery
                .OrderByDescending(x => x.Batch.BatchDate)
                .ThenByDescending(x => x.Batch.Id)
                .ToListAsync(ct);

            var batchIds = batchData.Select(x => x.Batch.Id).ToList();

            var logsQuery = _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x => batchIds.Contains(x.NoticeBatchId));

            if (filter.Status.HasValue)
                logsQuery = logsQuery.Where(x => x.Status == filter.Status.Value);

            if (!string.IsNullOrWhiteSpace(filter.ReferenceNo))
            {
                var refNo = filter.ReferenceNo.Trim();

                logsQuery = logsQuery.Where(x =>
                    (x.ObjectionNo != null && x.ObjectionNo.Contains(refNo)) ||
                    (x.AppealNo != null && x.AppealNo.Contains(refNo)) ||
                    (x.PremiseId != null && x.PremiseId.Contains(refNo)));
            }

            if (!string.IsNullOrWhiteSpace(filter.RecipientEmail))
            {
                var email = filter.RecipientEmail.Trim();
                logsQuery = logsQuery.Where(x => x.RecipientEmail != null && x.RecipientEmail.Contains(email));
            }

            if (!string.IsNullOrWhiteSpace(filter.SentBy))
            {
                var sentBy = filter.SentBy.Trim();
                logsQuery = logsQuery.Where(x => x.SentBy != null && x.SentBy.Contains(sentBy));
            }

            var logs = await logsQuery.ToListAsync(ct);

            var logsByBatch = logs
                .GroupBy(x => x.NoticeBatchId)
                .ToDictionary(x => x.Key, x => x.ToList());

            var vm = new NoticeStatsDashboardVm
            {
                Filter = filter,

                TotalBatches = batchData.Count,
                TotalRecords = logs.Count,
                TotalGenerated = logs.Count(x => x.Status == RunStatus.Generated),
                TotalPrinted = logs.Count(x => x.Status == RunStatus.Printed),
                TotalSent = logs.Count(x => x.Status == RunStatus.Sent),
                TotalFailed = logs.Count(x => x.Status == RunStatus.Failed),
                TotalNoEmail = logs.Count(x => x.Status == RunStatus.NoEmail)
            };

            vm.Batches = batchData.Select(x =>
            {
                logsByBatch.TryGetValue(x.Batch.Id, out var batchLogs);
                batchLogs ??= new List<Domain.Workflow.Entities.NoticeRunLog>();

                return new NoticeStatsBatchRowVm
                {
                    WorkflowKey = x.Batch.WorkflowKey,

                    BatchId = x.Batch.Id,
                    BatchName = x.Batch.BatchName,

                    RollId = x.Batch.RollId,
                    RollShortCode = x.Roll.ShortCode ?? "",
                    RollName = x.Roll.Name ?? "",

                    Notice = x.Settings.Notice,
                    VersionText = $"V{x.Settings.Version}",

                    BatchDate = x.Batch.BatchDate,

                    TotalRecords = batchLogs.Count,
                    Generated = batchLogs.Count(l => l.Status == RunStatus.Generated),
                    Printed = batchLogs.Count(l => l.Status == RunStatus.Printed),
                    Sent = batchLogs.Count(l => l.Status == RunStatus.Sent),
                    Failed = batchLogs.Count(l => l.Status == RunStatus.Failed),
                    NoEmail = batchLogs.Count(l => l.Status == RunStatus.NoEmail),

                    LastSentAtUtc = batchLogs
                        .Where(l => l.SentAtUtc.HasValue)
                        .OrderByDescending(l => l.SentAtUtc)
                        .Select(l => l.SentAtUtc)
                        .FirstOrDefault(),

                    SentBy = batchLogs
                        .Where(l => !string.IsNullOrWhiteSpace(l.SentBy))
                        .OrderByDescending(l => l.SentAtUtc)
                        .Select(l => l.SentBy)
                        .FirstOrDefault()
                };
            })
            .Where(x => x.TotalRecords > 0 || !filter.Status.HasValue)
            .ToList();

            await LoadOptionsAsync(vm, ct);

            return vm;
        }

        public async Task<NoticeStatsDetailsVm> BuildDetailsAsync(
            int batchId,
            CancellationToken ct)
        {
            var batchData = await (
                from b in _db.NoticeBatches.AsNoTracking()
                join s in _db.NoticeSettings.AsNoTracking()
                    on b.NoticeSettingsId equals s.Id
                join r in _db.RollRegistry.AsNoTracking()
                    on b.RollId equals r.RollId
                where b.Id == batchId
                select new
                {
                    Batch = b,
                    Settings = s,
                    Roll = r
                })
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("Batch not found.");

            var logs = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x => x.NoticeBatchId == batchId)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);

            return new NoticeStatsDetailsVm
            {
                WorkflowKey = batchData.Batch.WorkflowKey,
                BatchId = batchData.Batch.Id,
                BatchName = batchData.Batch.BatchName,

                RollShortCode = batchData.Roll.ShortCode ?? "",
                RollName = batchData.Roll.Name ?? "",

                Notice = batchData.Settings.Notice,
                VersionText = $"V{batchData.Settings.Version}",

                Rows = logs.Select(x => new NoticeStatsDetailRowVm
                {
                    ObjectionNo = x.ObjectionNo,
                    AppealNo = x.AppealNo,
                    PremiseId = x.PremiseId,

                    PropertyDesc = x.PropertyDesc,
                    RecipientName = x.RecipientName,
                    RecipientEmail = x.RecipientEmail,

                    Status = x.Status,
                    ErrorMessage = x.ErrorMessage,

                    PdfPath = x.PdfPath,
                    EmlPath = x.EmlPath,

                    SentAtUtc = x.SentAtUtc,
                    SentBy = x.SentBy
                }).ToList()
            };
        }

        private async Task LoadOptionsAsync(
            NoticeStatsDashboardVm vm,
            CancellationToken ct)
        {
            vm.RollOptions = await _db.RollRegistry
                .AsNoTracking()
                .OrderBy(x => x.ShortCode)
                .Select(x => new SelectListItem
                {
                    Value = x.RollId.ToString(),
                    Text = (x.ShortCode ?? "") + " - " + (x.Name ?? "")
                })
                .ToListAsync(ct);

            vm.RollOptions.Insert(0, new SelectListItem
            {
                Value = "",
                Text = "All Rolls"
            });

            vm.NoticeOptions = Enum.GetValues<NoticeKind>()
                .Select(x => new SelectListItem
                {
                    Value = ((int)x).ToString(),
                    Text = x.ToString()
                })
                .ToList();

            vm.NoticeOptions.Insert(0, new SelectListItem
            {
                Value = "",
                Text = "All Notices"
            });

            vm.StatusOptions = Enum.GetValues<RunStatus>()
                .Select(x => new SelectListItem
                {
                    Value = ((int)x).ToString(),
                    Text = x.ToString()
                })
                .ToList();

            vm.StatusOptions.Insert(0, new SelectListItem
            {
                Value = "",
                Text = "All Statuses"
            });
        }
    }
}
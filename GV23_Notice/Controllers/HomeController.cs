using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Claims;

namespace GV23_Notice.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;

        public HomeController(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            ViewBag.Username = User.Identity?.Name ?? "";
            ViewBag.FullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "";
            ViewBag.Role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";

            // ── Live stats ──────────────────────────────────────────────────
            var settings = await _db.NoticeSettings.AsNoTracking().ToListAsync(ct);

            var activeRolls = await _db.RollRegistry.AsNoTracking().CountAsync(ct);

            var totalBatches = await _db.NoticeBatches.AsNoTracking()
                .CountAsync(b => b.BatchKind == "STEP3", ct);

            var totalPrinted = await _db.NoticeRunLogs.AsNoTracking()
                .CountAsync(r => r.Status == RunStatus.Printed, ct);

            var totalSent = await _db.NoticeRunLogs.AsNoTracking()
                .CountAsync(r => r.Status == RunStatus.Sent, ct);

            // ── Notice type stats, including S53Rev ─────────────────────────
            var noticeStats = settings
                .GroupBy(s => s.Notice)
                .Select(g => new NoticeTypeStatVm
                {
                    Notice = g.Key,
                    NoticeName = NoticeDisplayName(g.Key),
                    TotalWorkflows = g.Count(),
                    Step1Confirmed = g.Count(x => x.IsConfirmed),
                    Step1Approved = g.Count(x => x.IsApproved),
                    PendingStep2 = g.Count(x => x.IsApproved && !x.IsStep2Approved),
                    Step2Approved = g.Count(x => x.IsStep2Approved)
                })
                .OrderBy(x => NoticeSortOrder(x.Notice))
                .ToList();

            // Force all notice cards to show, even when count is 0
            var allNoticeKinds = new[]
            {
        NoticeKind.S49,
        NoticeKind.S51,
        NoticeKind.S52,
        NoticeKind.S53,
        NoticeKind.S53Rev,
        NoticeKind.DJ,
        NoticeKind.IN,
        NoticeKind.S78
    };

            foreach (var notice in allNoticeKinds)
            {
                if (!noticeStats.Any(x => x.Notice == notice))
                {
                    noticeStats.Add(new NoticeTypeStatVm
                    {
                        Notice = notice,
                        NoticeName = NoticeDisplayName(notice),
                        TotalWorkflows = 0,
                        Step1Confirmed = 0,
                        Step1Approved = 0,
                        PendingStep2 = 0,
                        Step2Approved = 0
                    });
                }
            }

            noticeStats = noticeStats
                .OrderBy(x => NoticeSortOrder(x.Notice))
                .ToList();

            // ── Batch/run stats by notice type ──────────────────────────────
            var batchStats = await _db.NoticeBatches.AsNoTracking()
                .Where(b => b.BatchKind == "STEP3")
                .GroupBy(b => b.Notice)
                .Select(g => new
                {
                    Notice = g.Key,
                    BatchCount = g.Count()
                })
                .ToListAsync(ct);

            foreach (var stat in noticeStats)
            {
                var batch = batchStats.FirstOrDefault(x => x.Notice == stat.Notice);
                stat.BatchCount = batch?.BatchCount ?? 0;
            }

            var runStats = await _db.NoticeRunLogs.AsNoTracking()
                .Include(r => r.NoticeBatch)
                .Where(r => r.NoticeBatch.BatchKind == "STEP3")
                .GroupBy(r => r.NoticeBatch.Notice)
                .Select(g => new
                {
                    Notice = g.Key,
                    Printed = g.Count(x => x.Status == RunStatus.Printed),
                    Sent = g.Count(x => x.Status == RunStatus.Sent),
                    Failed = g.Count(x => x.Status == RunStatus.Failed)
                })
                .ToListAsync(ct);

            foreach (var stat in noticeStats)
            {
                var run = runStats.FirstOrDefault(x => x.Notice == stat.Notice);
                stat.PrintedCount = run?.Printed ?? 0;
                stat.SentCount = run?.Sent ?? 0;
                stat.FailedCount = run?.Failed ?? 0;
            }

            // ── Recent workflows ────────────────────────────────────────────
            var recentSettings = await _db.NoticeSettings.AsNoTracking()
                .Include(s => s.Batches)
                .OrderByDescending(s => s.Id)
                .Take(10)
                .ToListAsync(ct);

            var rollIds = recentSettings.Select(s => s.RollId).Distinct().ToList();

            var rolls = await _db.RollRegistry.AsNoTracking()
                .Where(r => rollIds.Contains(r.RollId))
                .ToDictionaryAsync(r => r.RollId, ct);

            var settingsBatchIds = recentSettings
                .SelectMany(s => s.Batches.Select(b => b.Id))
                .ToList();

            var sentByBatch = settingsBatchIds.Count > 0
                ? await _db.NoticeRunLogs.AsNoTracking()
                    .Where(r => settingsBatchIds.Contains(r.NoticeBatchId) && r.Status == RunStatus.Sent)
                    .GroupBy(r => r.NoticeBatchId)
                    .Select(g => new { BatchId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.BatchId, x => x.Count, ct)
                : new Dictionary<int, int>();

            var rows = recentSettings.Select(s =>
            {
                rolls.TryGetValue(s.RollId, out var roll);

                var sent = s.Batches.Sum(b =>
                    sentByBatch.TryGetValue(b.Id, out var c) ? c : 0);

                return new RecentWorkflowRow
                {
                    Id = s.Id,
                    RollName = roll?.Name ?? "Unknown Roll",
                    RollShortCode = roll?.ShortCode ?? "?",
                    Notice = s.Notice,
                    Version = s.Version,
                    LetterDate = s.LetterDate,
                    IsConfirmed = s.IsConfirmed,
                    IsApproved = s.IsApproved,
                    IsStep2Approved = s.IsStep2Approved,
                    HasWorkflowKey = s.WorkflowKey.HasValue,
                    BatchCount = s.Batches.Count,
                    SentCount = sent,
                    WorkflowKey = s.WorkflowKey
                };
            }).ToList();

            var vm = new HomeIndexVm
            {
                ActiveRolls = activeRolls,
                TotalWorkflows = settings.Count,
                Step1Confirmed = settings.Count(s => s.IsConfirmed),
                Step1Approved = settings.Count(s => s.IsApproved),
                PendingStep2 = settings.Count(s => s.IsApproved && !s.IsStep2Approved),
                Step2Approved = settings.Count(s => s.IsStep2Approved),
                TotalBatches = totalBatches,
                TotalPrinted = totalPrinted,
                TotalSent = totalSent,
                RecentWorkflows = rows,

                NoticeStats = noticeStats
            };

            return View(vm);
        }

        private static string NoticeDisplayName(NoticeKind notice)
        {
            return notice switch
            {
                NoticeKind.S49 => "Section 49",
                NoticeKind.S51 => "Section 51",
                NoticeKind.S52 => "Section 52",
                NoticeKind.S53 => "Section 53 MVD",
                NoticeKind.S53Rev => "Section 53 Revised MVD",
                NoticeKind.DJ => "Dear Johnny",
                NoticeKind.IN => "Invalidity Notices",
                NoticeKind.S78 => "Section 78",
                _ => notice.ToString()
            };
        }

        private static int NoticeSortOrder(NoticeKind notice)
        {
            return notice switch
            {
                NoticeKind.S49 => 1,
                NoticeKind.S51 => 2,
                NoticeKind.S52 => 3,
                NoticeKind.S53 => 4,
                NoticeKind.S53Rev => 5,
                NoticeKind.DJ => 6,
                NoticeKind.IN => 7,
                NoticeKind.S78 => 8,
                _ => 99
            };
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
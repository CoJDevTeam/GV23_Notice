using System.Diagnostics;
using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
            // ── Live stats ──────────────────────────────────────────────────
            var settings = await _db.NoticeSettings.AsNoTracking().ToListAsync(ct);

            var activeRolls = await _db.RollRegistry.AsNoTracking().CountAsync(ct);
            var totalBatches = await _db.NoticeBatches.AsNoTracking().CountAsync(b => b.BatchKind == "STEP3", ct);
            var totalPrinted = await _db.NoticeRunLogs.AsNoTracking()
                                     .CountAsync(r => r.Status == RunStatus.Printed, ct);
            var totalSent = await _db.NoticeRunLogs.AsNoTracking()
                                     .CountAsync(r => r.Status == RunStatus.Sent, ct);

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

            // Sent counts per settings id
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
                var sent = s.Batches.Sum(b => sentByBatch.TryGetValue(b.Id, out var c) ? c : 0);
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
                RecentWorkflows = rows
            };

            return View(vm);
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
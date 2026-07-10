using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.Stats;
using GV23_Notice.Services.Stats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GV23_Notice.Controllers
{
    [Authorize]
    [Route("[controller]")]
    public sealed class StatsController : Controller
    {
        private readonly INoticeStatsDashboardService _stats;
        private readonly IThirdPartyAppealStatsService _tpaStats;

        public StatsController(
            INoticeStatsDashboardService stats,
            IThirdPartyAppealStatsService tpaStats)
        {
            _stats = stats;
            _tpaStats = tpaStats;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(
            [FromQuery] NoticeStatsFilterVm filter,
            CancellationToken ct)
        {
            var vm = await _stats.BuildDashboardAsync(filter, ct);

            vm.IsTpa = filter.Notice == NoticeKind.TPA;
            vm.WorkflowKey = filter.WorkflowKey;

            if (vm.IsTpa)
            {
                if (!filter.WorkflowKey.HasValue ||
                    filter.WorkflowKey.Value == Guid.Empty)
                {
                    TempData["Error"] =
                        "The TPA workflow key was not supplied.";

                    return View(vm);
                }

                var tpaStats = await _tpaStats.BuildStatsAsync(
                    filter.WorkflowKey.Value,
                    ct);

                vm.TpaStats = tpaStats;
                vm.TotalBatches = tpaStats.TotalAdmins;
                vm.TotalRecords = tpaStats.TotalRecords;
                vm.TotalGenerated = 0;
                vm.TotalPrinted = tpaStats.TotalPrinted;
                vm.TotalSent = tpaStats.TotalSent;
                vm.TotalFailed = tpaStats.TotalFailed;
                vm.TotalNoEmail = tpaStats.TotalNoEmail;
                vm.Batches.Clear();
            }

            return View(vm);
        }

        [HttpGet("Details/{batchId:int}")]
        public async Task<IActionResult> Details(
            int batchId,
            CancellationToken ct)
        {
            var vm = await _stats.BuildDetailsAsync(batchId, ct);
            return View(vm);
        }

        [HttpGet("TPA")]
        public IActionResult Tpa(Guid key)
        {
            if (key == Guid.Empty)
            {
                return BadRequest("Invalid workflow key.");
            }

            return RedirectToAction(
                nameof(Index),
                new
                {
                    Notice = NoticeKind.TPA,
                    WorkflowKey = key
                });
        }

        [HttpGet("TPA/DownloadAdminExcel")]
        public async Task<IActionResult> DownloadTpaAdminExcel(
            Guid key,
            string adminKey,
            CancellationToken ct)
        {
            if (key == Guid.Empty)
            {
                return BadRequest("Invalid workflow key.");
            }

            if (string.IsNullOrWhiteSpace(adminKey))
            {
                return BadRequest("Invalid Admin key.");
            }

            try
            {
                var file = await _tpaStats.BuildAdminExcelAsync(
                    key,
                    adminKey,
                    ct);

                return File(
                    file.Content,
                    file.ContentType,
                    file.FileName);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;

                return RedirectToTpaStats(key);
            }
        }

        [HttpPost("TPA/SendAdminReport")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendTpaAdminReport(
            Guid key,
            string adminKey,
            CancellationToken ct)
        {
            if (key == Guid.Empty)
            {
                return BadRequest("Invalid workflow key.");
            }

            if (string.IsNullOrWhiteSpace(adminKey))
            {
                return BadRequest("Invalid Admin key.");
            }

            var performedBy =
                User?.Identity?.Name ?? "Unknown";

            try
            {
                var result =
                    await _tpaStats.SendAdminReportAsync(
                        key,
                        adminKey,
                        performedBy,
                        ct);

                if (result.Success)
                {
                    TempData["Success"] =
                        $"The TPA statistics report was sent to {result.AdminName} ({result.AdminEmail}).";
                }
                else
                {
                    TempData["Error"] =
                        $"The TPA statistics report for {result.AdminName} was not sent. {result.ErrorMessage}";
                }
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToTpaStats(key);
        }

        [HttpPost("TPA/SendAllAdminReports")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendAllTpaAdminReports(
            Guid key,
            CancellationToken ct)
        {
            if (key == Guid.Empty)
            {
                return BadRequest("Invalid workflow key.");
            }

            var performedBy =
                User?.Identity?.Name ?? "Unknown";

            try
            {
                var result =
                    await _tpaStats.SendAllAdminReportsAsync(
                        key,
                        performedBy,
                        ct);

                TempData["Success"] =
                    $"TPA Admin report process completed. " +
                    $"Sent: {result.Sent}, " +
                    $"Failed: {result.Failed}, " +
                    $"Skipped: {result.Skipped}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] =
                    $"The bulk TPA Admin report process failed. {ex.Message}";
            }

            return RedirectToTpaStats(key);
        }

        private IActionResult RedirectToTpaStats(Guid key)
        {
            return RedirectToAction(
                nameof(Index),
                new
                {
                    Notice = NoticeKind.TPA,
                    WorkflowKey = key
                });
        }
    }
}
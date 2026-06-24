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

        public StatsController(INoticeStatsDashboardService stats)
        {
            _stats = stats;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(
            [FromQuery] NoticeStatsFilterVm filter,
            CancellationToken ct)
        {
            var vm = await _stats.BuildDashboardAsync(filter, ct);
            return View(vm);
        }

        [HttpGet("Details/{batchId:int}")]
        public async Task<IActionResult> Details(int batchId, CancellationToken ct)
        {
            var vm = await _stats.BuildDetailsAsync(batchId, ct);
            return View(vm);
        }
    }
}
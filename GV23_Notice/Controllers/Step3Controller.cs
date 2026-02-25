using GV23_Notice.Domain.Workflow;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GV23_Notice.Controllers
{
    [Authorize]
    [Route("Step3")]
    public sealed class Step3Controller : Controller
    {
        private readonly IStep3Step1Service _svc;
        private readonly IStep3WorkflowSelectService _select;
        private readonly IStep3BatchQueryService _batchQuery;
        private readonly IStep3BatchService _batchCreate;
        private readonly INoticeBatchPrintService _print;

        public Step3Controller(
            IStep3Step1Service svc,
            IStep3WorkflowSelectService select,
            IStep3BatchQueryService batchQuery,
            IStep3BatchService batchCreate,
            INoticeBatchPrintService print)
        {
            _svc = svc;
            _select = select;
            _batchQuery = batchQuery;
            _batchCreate = batchCreate;
            _print = print;
        }

        // GET: /Step3
        [HttpGet("")]
        public async Task<IActionResult> Index(int? rollId, NoticeKind? notice, CancellationToken ct)
        {
            var vm = await _select.BuildAsync(rollId, notice, ct);
            return View(vm);
        }

        // GET: /Step3/Open?key=...
        [HttpGet("Open")]
        public IActionResult Open(Guid key)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            return RedirectToAction(nameof(Step1), new { key });
        }

        // GET: /Step3/Step1?key=...
        // Readonly summary (same info as Step2, but readonly)
        [HttpGet("Step1")]
        public async Task<IActionResult> Step1(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");

            var vm = await _svc.BuildAsync(key, ct);
            return View(vm);
        }

        // GET: /Step3/Kickoff?key=...
        // If your "Kickoff" view is supposed to look like Step2 readonly, route it there.
        [HttpGet("Kickoff")]
        public IActionResult Kickoff(Guid key)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            return RedirectToAction(nameof(Step2), new { key });
        }

        // GET: /Step3/Step2?key=...
        // Batch dashboard: counts, existing batches, progress, etc.
        [HttpGet("Step2")]
        public async Task<IActionResult> Step2(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");

            var vm = await _batchQuery.BuildAsync(key, ct);
            return View(vm);
        }

        // POST: /Step3/Step2CreateBatch
        // Creates a NoticeBatch row + (for S49) assigns TOP 500 PREMISEIDs -> Batch_Name/Batch_Date in roll table.
        [HttpPost("Step2CreateBatch")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2CreateBatch(Guid key, DateTime? batchDate, CancellationToken ct)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");

            var user = User?.Identity?.Name ?? "Unknown";

            // default to today if not supplied
            var date = (batchDate ?? DateTime.Today).Date;

            var result = await _batchCreate.CreateBatchAsync(key, date, user, ct);

          
            return RedirectToAction(nameof(Step2), new { key });
        }

        // POST: /Step3/Print
        [HttpPost("Print")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Print(int batchId, Guid key, CancellationToken ct)
        {
            var user = User?.Identity?.Name ?? "Unknown";
            var res = await _print.PrintBatchAsync(batchId, user, ct);

            TempData["Success"] = $"Printed: {res.Printed}, Failed: {res.Failed} (Total {res.Total})";
            return RedirectToAction(nameof(Step2), new { key });
        }
    }
}
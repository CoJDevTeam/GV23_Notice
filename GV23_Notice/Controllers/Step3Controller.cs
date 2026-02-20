using GV23_Notice.Domain.Workflow;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using System.Drawing;
using System.Runtime.Intrinsics.X86;

namespace GV23_Notice.Controllers
{
    public class Step3Controller : Controller
    {
        private readonly IStep3Step1Service _svc;
        private readonly IStep3WorkflowSelectService _select;
        private readonly IStep3BatchQueryService _batchQuery;
        private readonly IStep3BatchService _batchCreate;
        private readonly INoticeBatchPrintService _print;

        public Step3Controller(IStep3Step1Service svc, IStep3WorkflowSelectService select,IStep3BatchQueryService batchQuery,
     IStep3BatchService batchCreate, INoticeBatchPrintService print)
        {
            _svc = svc;
            _select = select;
            _batchQuery = batchQuery;
               _batchCreate = batchCreate;
            _print = print;
        }
        // GET: /Step3
        [HttpGet]
        public async Task<IActionResult> Index(int? rollId, NoticeKind? notice, CancellationToken ct)
        {
            var vm = await _select.BuildAsync(rollId, notice, ct);
            return View(vm);
        }

        // GET: /Step3/Open?key=...
        [HttpGet]
        public IActionResult Open(Guid key)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            return RedirectToAction(nameof(Step1), new { key });
        }
        // GET: /Step3/Step1?key=...
        [HttpGet]
        public async Task<IActionResult> Step1(Guid key, CancellationToken ct)
        {
            //if (key == Guid.Empty) return BadRequest("Invalid workflow key.");

            var vm = await _svc.BuildAsync(key, ct);
            return View(vm);
        }

        // GET: /Step3/Kickoff?key=...
        [HttpGet]
        public IActionResult Kickoff(Guid key)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            return RedirectToAction(nameof(Step1), new { key });
        }
        [HttpGet]
        public async Task<IActionResult> Step2(Guid key, CancellationToken ct)
        {
            var vm = await _batchQuery.BuildAsync(key, ct);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2CreateBatch(Guid key, DateTime batchDate, CancellationToken ct)
        {
            var user = User?.Identity?.Name ?? "Unknown";
            await _batchCreate.CreateBatchAsync(key, batchDate, user, ct);

            TempData["Success"] = "Batch created successfully.";
            return RedirectToAction(nameof(Step2), new { key });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step3Print(int batchId, CancellationToken ct)
        {
            var user = User?.Identity?.Name ?? "Unknown";
            var res = await _print.PrintBatchAsync(batchId, user, ct);

            TempData["Success"] = $"Printed: {res.Printed}, Failed: {res.Failed} (Total {res.Total})";
            return View();
           // return RedirectToAction(nameof(Step3), new { batchId });
        }
    }
}

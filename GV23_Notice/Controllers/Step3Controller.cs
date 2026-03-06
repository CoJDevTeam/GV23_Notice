using GV23_Notice.Data;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        private readonly IStep3PrintQueryService _printQuery;
        private readonly INoticeBatchEmailService _emailSvc;
        private readonly AppDbContext _db;

        public Step3Controller(
            IStep3Step1Service svc,
            IStep3WorkflowSelectService select,
            IStep3BatchQueryService batchQuery,
            IStep3BatchService batchCreate,
            INoticeBatchPrintService print,
            IStep3PrintQueryService printQuery,
            INoticeBatchEmailService emailSvc,
            AppDbContext db)
        {
            _svc = svc;
            _select = select;
            _batchQuery = batchQuery;
            _batchCreate = batchCreate;
            _print = print;
            _printQuery = printQuery;
            _emailSvc = emailSvc;
            _db = db;
        }

        // ── Index ───────────────────────────────────────────────────────────
        [HttpGet("")]
        public async Task<IActionResult> Index(int? rollId, Domain.Workflow.NoticeKind? notice, CancellationToken ct)
        {
            var vm = await _select.BuildAsync(rollId, notice, ct);
            return View(vm);
        }

        // ── Step1 (readonly summary) ────────────────────────────────────────
        [HttpGet("Open")]
        public IActionResult Open(Guid key)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            return RedirectToAction(nameof(Step1), new { key });
        }

        [HttpGet("Step1")]
        public async Task<IActionResult> Step1(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            var vm = await _svc.BuildAsync(key, ct);
            return View(vm);
        }

        [HttpGet("Kickoff")]
        public IActionResult Kickoff(Guid key)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            return RedirectToAction(nameof(Step2), new { key });
        }

        // ── Step2 — Batch Summary & Print Dashboard ─────────────────────────
        [HttpGet("Step2")]
        public async Task<IActionResult> Step2(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            var vm = await _batchQuery.BuildAsync(key, ct);
            return View(vm);
        }

        // POST: Create a new batch
        [HttpPost("Step2CreateBatch")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2CreateBatch(Guid key, DateTime? batchDate, CancellationToken ct)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            var user = User?.Identity?.Name ?? "Unknown";
            var date = (batchDate ?? DateTime.Today).Date;
            await _batchCreate.CreateBatchAsync(key, date, user, ct);
            TempData["Success"] = "Batch created successfully.";
            return RedirectToAction(nameof(Step2), new { key });
        }

        // ── PRINT ───────────────────────────────────────────────────────────

        // GET: Print dashboard
        [HttpGet("Print")]
        public async Task<IActionResult> Print(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            var vm = await _printQuery.BuildPrintVmAsync(key, ct);
            return View(vm);
        }

        // POST: Print a single batch
        [HttpPost("PrintBatch")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrintBatch(int batchId, Guid key, CancellationToken ct)
        {
            var user = User?.Identity?.Name ?? "Unknown";
            var res = await _print.PrintBatchAsync(batchId, user, ct);
            TempData["Success"] = $"Batch printed: {res.Printed} notices saved. " +
                                  (res.Failed > 0 ? $"{res.Failed} failed." : "");
            return RedirectToAction(nameof(Print), new { key });
        }

        // POST: Print ALL batches in this workflow
        [HttpPost("PrintAll")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrintAll(Guid key, CancellationToken ct)
        {
            var user = User?.Identity?.Name ?? "Unknown";
            var res = await _print.PrintAllBatchesAsync(key, user, ct);
            TempData["Success"] = $"All batches printed: {res.Printed} notices saved across {res.TotalBatches} batches. " +
                                  (res.Failed > 0 ? $"{res.Failed} failed." : "");
            return RedirectToAction(nameof(Print), new { key });
        }

        // ── SEND EMAIL ──────────────────────────────────────────────────────

        // GET: Send Email dashboard
        [HttpGet("SendEmail")]
        public async Task<IActionResult> SendEmail(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            var vm = await _printQuery.BuildEmailVmAsync(key, ct);
            return View(vm);
        }

        // POST: Send emails for selected batches
        [HttpPost("SendEmail")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendEmail(
            Guid key,
            [FromForm(Name = "batchIds")] List<int> batchIds,
            CancellationToken ct)
        {
            if (key == Guid.Empty) return BadRequest("Invalid workflow key.");
            if (batchIds == null || batchIds.Count == 0)
            {
                TempData["Error"] = "Please select at least one batch to send.";
                return RedirectToAction(nameof(SendEmail), new { key });
            }

            var user = User?.Identity?.Name ?? "Unknown";
            var res = await _emailSvc.SendBatchEmailsAsync(batchIds, key, user, ct);

            if (!string.IsNullOrWhiteSpace(res.ErrorMessage))
                TempData["Error"] = res.ErrorMessage;
            else
                TempData["Success"] = $"Emails sent: {res.Sent}. " +
                                      (res.Failed > 0 ? $"Failed: {res.Failed}. " : "") +
                                      (res.Skipped > 0 ? $"No email address: {res.Skipped}." : "");

            return RedirectToAction(nameof(SendEmail), new { key });
        }

        // GET: Live progress across ALL batches in a workflow
        [HttpGet("PrintProgressAll")]
        public async Task<IActionResult> PrintProgressAll(Guid key, CancellationToken ct)
        {
            var batchIds = await _db.NoticeBatches
                .Where(b => b.WorkflowKey == key && b.BatchKind == "STEP3")
                .Select(b => b.Id)
                .ToListAsync(ct);

            if (batchIds.Count == 0)
                return Json(new { total = 0, printed = 0, sent = 0, failed = 0, generated = 0, done = true });

            var counts = await _db.NoticeRunLogs
                .Where(r => batchIds.Contains(r.NoticeBatchId))
                .GroupBy(r => r.Status)
                .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var total = counts.Sum(x => x.Count);
            var printed = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Printed)?.Count ?? 0;
            var sent = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Sent)?.Count ?? 0;
            var failed = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Failed)?.Count ?? 0;
            var generated = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Generated)?.Count ?? 0;
            var done = generated == 0 && total > 0;

            return Json(new { total, printed, sent, failed, generated, done });
        }

        // GET: Live progress for a batch being printed (polled by JS)
        [HttpGet("PrintProgress")]
        public async Task<IActionResult> PrintProgress(int batchId, CancellationToken ct)
        {
            var counts = await _db.NoticeRunLogs
                .Where(r => r.NoticeBatchId == batchId)
                .GroupBy(r => r.Status)
                .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var total = counts.Sum(x => x.Count);
            var printed = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Printed)?.Count ?? 0;
            var sent = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Sent)?.Count ?? 0;
            var failed = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Failed)?.Count ?? 0;
            var generated = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Generated)?.Count ?? 0;

            var done = generated == 0 && (printed + sent + failed) >= total && total > 0;

            return Json(new { total, printed, sent, failed, generated, done });
        }

        // AJAX: count records in selected batches (for live UI cap check)
        [HttpPost("CountSelected")]
        public async Task<IActionResult> CountSelected(
            [FromForm(Name = "batchIds")] List<int> batchIds,
            CancellationToken ct)
        {
            var count = await _emailSvc.CountSelectedRecordsAsync(batchIds ?? new(), ct);
            return Json(new { count, over = count > 2000 });
        }

        // GET: Live email progress for a single batch (polled by JS)
        [HttpGet("EmailProgress")]
        public async Task<IActionResult> EmailProgress(int batchId, CancellationToken ct)
        {
            var counts = await _db.NoticeRunLogs
                .Where(r => r.NoticeBatchId == batchId)
                .GroupBy(r => r.Status)
                .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var printed = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Printed)?.Count ?? 0;
            var sent = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Sent)?.Count ?? 0;
            var failed = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Failed)?.Count ?? 0;
            var noEmail = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.NoEmail)?.Count ?? 0;
            var total = counts.Sum(x => x.Count);
            var done = printed == 0 && (sent + failed + noEmail) >= total && total > 0;

            return Json(new { total, printed, sent, failed, noEmail, done });
        }

        // GET: Live email progress across ALL selected batches in a workflow
        [HttpGet("EmailProgressAll")]
        public async Task<IActionResult> EmailProgressAll(Guid key, CancellationToken ct)
        {
            var batchIds = await _db.NoticeBatches
                .Where(b => b.WorkflowKey == key && b.BatchKind == "STEP3")
                .Select(b => b.Id).ToListAsync(ct);

            if (batchIds.Count == 0)
                return Json(new { total = 0, printed = 0, sent = 0, failed = 0, noEmail = 0, done = true });

            var counts = await _db.NoticeRunLogs
                .Where(r => batchIds.Contains(r.NoticeBatchId))
                .GroupBy(r => r.Status)
                .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var printed = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Printed)?.Count ?? 0;
            var sent = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Sent)?.Count ?? 0;
            var failed = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.Failed)?.Count ?? 0;
            var noEmail = counts.FirstOrDefault(x => x.Status == (int)Domain.Workflow.RunStatus.NoEmail)?.Count ?? 0;
            var total = counts.Sum(x => x.Count);
            var done = printed == 0 && total > 0;

            return Json(new { total, printed, sent, failed, noEmail, done });
        }
    }
}
using GV23_Notice.Data;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.QA;
using GV23_Notice.Services.Stats;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mono.TextTemplating;

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
        private readonly IS52RangePrintService _s52RangePrint;
        private readonly INoticeQaService _qa;
        private readonly INoticeSendStatsService _stats;
        public Step3Controller(
            IStep3Step1Service svc,
            IStep3WorkflowSelectService select,
            IStep3BatchQueryService batchQuery,
            IStep3BatchService batchCreate,
            INoticeBatchPrintService print,
            IStep3PrintQueryService printQuery,
            INoticeBatchEmailService emailSvc,
            IS52RangePrintService s52RangePrint,
            AppDbContext db, INoticeQaService qa,INoticeSendStatsService statsService)
        {
            _svc = svc;
            _select = select;
            _batchQuery = batchQuery;
            _batchCreate = batchCreate;
            _stats = statsService;
            _print = print;
            _printQuery = printQuery;
            _emailSvc = emailSvc;
            _db = db;
            _s52RangePrint = s52RangePrint;
            _qa = qa;
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

            // Resolve settingsId so we can redirect back to the Kickoff page
            var s = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ApprovalKey == key || x.WorkflowKey == key, ct);

            if (s is null) return BadRequest("Workflow not found.");

            try
            {
                await _batchCreate.CreateBatchAsync(key, date, user, ct);
                TempData["Success"] = "Batch created successfully.";
                return RedirectToAction("Step3Kickoff", "Workflow", new { settingsId = s.Id, key, showBatches = true });
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Step3Kickoff", "Workflow", new { settingsId = s.Id, key });
            }
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

        [HttpPost("PrintBatch")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrintBatch(int batchId, Guid key, CancellationToken ct)
        {
            var user = User?.Identity?.Name ?? "Unknown";
            var res = await _print.PrintBatchAsync(batchId, user, ct);

            TempData["Success"] = $"Batch printed: {res.Printed} notices saved. " +
                                  (res.Failed > 0 ? $"{res.Failed} failed." : "");

            if (await _qa.RequiresQaAsync(key, ct))
                return RedirectToAction(nameof(QA), new { key });

            return RedirectToAction(nameof(Print), new { key });
        }

        [HttpPost("PrintS52Range")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrintS52Range(
    Guid key,
    int settingsId,
    bool isReview,
    CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var user = User?.Identity?.Name ?? "Unknown";

            try
            {
                var count = await _s52RangePrint.CountRangeAsync(settingsId, isReview, ct);

                if (count <= 0)
                {
                    TempData["Error"] =
                        "No Section 52 records were found for this date range. Please check the From Date and To Date.";

                    return RedirectToAction(nameof(Print), new { key });
                }

                var res = await _s52RangePrint.PrintRangeAsync(settingsId, isReview, user, ct);

                TempData["Success"] =
                    $"Section 52 range printed: {res.Printed} notices saved. " +
                    (res.Failed > 0 ? $"{res.Failed} failed." : "");

                return RedirectToAction(nameof(Print), new
                {
                    key = res.WorkflowKey == Guid.Empty ? key : res.WorkflowKey
                });
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Print), new { key });
            }
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

            if (await _qa.RequiresQaAsync(key, ct))
                return RedirectToAction(nameof(QA), new { key });

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
                //return RedirectToAction(nameof(SendStats), new { key });
            }

            var user = User?.Identity?.Name ?? "Unknown";
            var res = await _emailSvc.SendBatchEmailsAsync(batchIds, key, user, ct);

            if (!string.IsNullOrWhiteSpace(res.ErrorMessage))
                TempData["Error"] = res.ErrorMessage;
            else
                TempData["Success"] = $"Emails sent: {res.Sent}. " +
                                      (res.Failed > 0 ? $"Failed: {res.Failed}. " : "") +
                                      (res.Skipped > 0 ? $"No email address: {res.Skipped}." : "");

            //return RedirectToAction(nameof(SendEmail), new { key });
            return RedirectToAction(nameof(SendStats), new { key });
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
        // ── QA ───────────────────────────────────────────────────────────────

        [HttpGet("QA")]
        public async Task<IActionResult> QA(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var vm = await _qa.BuildQaVmAsync(key, ct);
            return View(vm);
        }

        [HttpPost("CreateQA")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateQA(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var user = User?.Identity?.Name ?? "Unknown";

            try
            {
                await _qa.CreateQaRunAsync(key, user, ct);
                TempData["Success"] = "QA sample created successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(QA), new { key });
        }

        [HttpPost("ApproveQA")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveQA(
            Guid key,
            int qaRunId,
            string? comment,
            CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var user = User?.Identity?.Name ?? "Unknown";

            try
            {
                await _qa.ApproveQaAsync(key, qaRunId, user, comment, ct);
                TempData["Success"] = "QA approved. You can now send notices.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(QA), new { key });
        }

        [HttpGet("OpenPdf")]
        public async Task<IActionResult> OpenPdf(int runLogId, CancellationToken ct)
        {
            var run = await _db.NoticeRunLogs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == runLogId, ct);

            if (run == null)
                return NotFound("Notice run log not found.");

            if (string.IsNullOrWhiteSpace(run.PdfPath))
                return NotFound("PDF path is empty.");

            if (!System.IO.File.Exists(run.PdfPath))
                return NotFound("PDF file not found.");

            var fileName = Path.GetFileName(run.PdfPath);
            var bytes = await System.IO.File.ReadAllBytesAsync(run.PdfPath, ct);

            return File(bytes, "application/pdf", fileName);
        }

        [HttpGet("SendStats")]
        public async Task<IActionResult> SendStats(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var vm = await _stats.BuildStatsAsync(key, ct);
            return View(vm);
        }

        [HttpGet("DownloadStatsExcel")]
        public async Task<IActionResult> DownloadStatsExcel(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var user = User?.Identity?.Name ?? "Unknown";
            var path = await _stats.GenerateExcelAsync(key, user, ct);

            var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
            var fileName = Path.GetFileName(path);

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        [HttpPost("SendStatsEmail")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendStatsEmail(
      Guid key,
      string toEmails,
      string? ccEmails,
      CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var user = User?.Identity?.Name ?? "Unknown";

            try
            {
                await _stats.SendStatsEmailAsync(key, toEmails, ccEmails, user, ct);

                TempData["Success"] = "Stats report sent to stakeholders successfully.";

                // After successful send, go back to Home Dashboard
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;

                // If it fails, stay on stats page so the user can fix emails/retry
                return RedirectToAction(nameof(SendStats), new { key });
            }
        }
    }
}
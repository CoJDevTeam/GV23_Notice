using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.QA;
using GV23_Notice.Services.Stats;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.Storage;
using GV23_Notice.Services.ThirdPartyApplications;
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
        private readonly IS52RangePrintService _s52RangePrint;
        private readonly INoticeQaService _qa;
        private readonly INoticeSendStatsService _stats;
        private readonly IThirdPartyAppealStatsService _tpaStats;

        // Third-Party Appeal Application
        private readonly IThirdPartyAppealPrintService _tpaPrint;
        private readonly IThirdPartyAppealEmailService _tpaEmail;

        public Step3Controller(
            IStep3Step1Service svc,
            IStep3WorkflowSelectService select,
            IStep3BatchQueryService batchQuery,
            IStep3BatchService batchCreate,
            INoticeBatchPrintService print,
            IStep3PrintQueryService printQuery,
            INoticeBatchEmailService emailSvc,
            IS52RangePrintService s52RangePrint,
            AppDbContext db,
            INoticeQaService qa,
            INoticeSendStatsService statsService,
            IThirdPartyAppealStatsService tpaStats,
            IThirdPartyAppealPrintService tpaPrint,
            IThirdPartyAppealEmailService tpaEmail)
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
            _tpaStats = tpaStats;

            _tpaPrint = tpaPrint;
            _tpaEmail = tpaEmail;
        }

        // ── Index ───────────────────────────────────────────────────────────
        [HttpGet("")]
        public async Task<IActionResult> Index(
            int? rollId,
            NoticeKind? notice,
            CancellationToken ct)
        {
            var vm = await _select.BuildAsync(rollId, notice, ct);
            return View(vm);
        }

        // ── Step1 readonly summary ──────────────────────────────────────────
        [HttpGet("Open")]
        public IActionResult Open(Guid key)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            return RedirectToAction(nameof(Step1), new { key });
        }

        [HttpGet("Step1")]
        public async Task<IActionResult> Step1(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var vm = await _svc.BuildAsync(key, ct);
            return View(vm);
        }

        [HttpGet("Kickoff")]
        public IActionResult Kickoff(Guid key)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            return RedirectToAction(nameof(Step2), new { key });
        }

        // ── Step2 batch summary / print dashboard ───────────────────────────
        [HttpGet("Step2")]
        public async Task<IActionResult> Step2(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var settings = await GetWorkflowSettingsAsync(key, ct);

            if (settings.Notice == NoticeKind.TPA)
            {
                /*
                 * TPA does not use normal NoticeBatches.
                 * Send user straight to Print dashboard.
                 */
                return RedirectToAction(nameof(Print), new { key });
            }

            var vm = await _batchQuery.BuildAsync(key, ct);
            return View(vm);
        }

        // POST: Create a new batch
        [HttpPost("Step2CreateBatch")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2CreateBatch(
            Guid key,
            DateTime? batchDate,
            CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var user = User?.Identity?.Name ?? "Unknown";
            var date = (batchDate ?? DateTime.Today).Date;

            var s = await GetWorkflowSettingsAsync(key, ct);

            if (s.Notice == NoticeKind.TPA)
            {
                TempData["Error"] = "Third-Party Appeal Application notices do not use batch creation. Go directly to Print.";
                return RedirectToAction(nameof(Print), new { key });
            }

            try
            {
                await _batchCreate.CreateBatchAsync(key, date, user, ct);
                TempData["Success"] = "Batch created successfully.";

                return RedirectToAction(
                    "Step3Kickoff",
                    "Workflow",
                    new { settingsId = s.Id, key, showBatches = true });
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;

                return RedirectToAction(
                    "Step3Kickoff",
                    "Workflow",
                    new { settingsId = s.Id, key });
            }
        }

        // ── PRINT ───────────────────────────────────────────────────────────

        [HttpGet("Print")]
        public async Task<IActionResult> Print(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ApprovalKey == key || x.WorkflowKey == key, ct)
                ?? throw new InvalidOperationException("Workflow settings not found.");

            if (settings.Notice == NoticeKind.TPA)
            {
                /*
                 * TPA does not use NoticeBatches.
                 * We still return the normal Step3PrintVm because Print.cshtml expects it.
                 * TPA-specific data goes through ViewBag.
                 */
                ViewBag.TpaPrint = await _tpaPrint.BuildPrintVmAsync(key, ct);

                var vm = await BuildTpaStep3PrintVmAsync(settings, key, ct);

                return View("Print", vm);
            }

            var normalVm = await _printQuery.BuildPrintVmAsync(key, ct);
            return View("Print", normalVm);
        }

        private async Task<Step3PrintVm> BuildTpaStep3PrintVmAsync(
    NoticeSettings settings,
    Guid key,
    CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == settings.RollId, ct);

            return new Step3PrintVm
            {
                WorkflowKey = key,
                SettingsId = settings.Id,

                RollId = settings.RollId,
                RollShortCode = roll?.ShortCode ?? "GV23",
                RollName = roll?.Name ?? settings.RollName ?? "General Valuation Roll 2023",

                Notice = settings.Notice,

                Version = settings.Version,
                VersionText = $"V{settings.Version}",

                LetterDate = settings.LetterDate,
                ObjectionStartDate = settings.ObjectionStartDate,
                ObjectionEndDate = settings.ObjectionEndDate,
                ExtensionDate = settings.ExtensionDate,

                BulkFromDate = settings.BulkFromDate,
                BulkToDate = settings.BulkToDate,

                IsS52 = false,
                S52IsReview = false,

                TotalBatches = 0,
                TotalPrinted = 0,
                TotalFailed = 0,

                Batches = new()
            };
        }

        [HttpPost("PrintBatch")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrintBatch(
            int batchId,
            Guid key,
            CancellationToken ct)
        {
            var settings = await GetWorkflowSettingsAsync(key, ct);

            if (settings.Notice == NoticeKind.TPA)
            {
                TempData["Error"] = "Third-Party Appeal Application notices do not use batch printing. Use Print Third-Party Appeal Applications.";
                return RedirectToAction(nameof(Print), new { key });
            }

            var user = User?.Identity?.Name ?? "Unknown";
            var res = await _print.PrintBatchAsync(batchId, user, ct);

            TempData["Success"] =
                $"Batch printed: {res.Printed} notices saved. " +
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

            var settings = await GetWorkflowSettingsAsync(key, ct);

            if (settings.Notice == NoticeKind.TPA)
            {
                TempData["Error"] = "Third-Party Appeal Application notices do not use S52 range printing.";
                return RedirectToAction(nameof(Print), new { key });
            }

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

        // POST: Print TPA notices directly. No ranges. No batches.
        [HttpPost("PrintThirdPartyAppealApplications")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrintThirdPartyAppealApplications(
     Guid key,
     bool forceReprint,
     CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var user = User?.Identity?.Name ?? "Unknown";

            try
            {
                var settings = await GetWorkflowSettingsAsync(key, ct);

                if (settings.Notice != NoticeKind.TPA)
                    return BadRequest("This print action is only for Third-Party Appeal Application notices.");

                var res = await _tpaPrint.PrintAsync(
                    key,
                    user,
                    forceReprint,
                    ct);

                if (!string.IsNullOrWhiteSpace(res.ErrorMessage))
                {
                    TempData["Error"] = res.ErrorMessage;
                    return RedirectToAction(nameof(Print), new { key });
                }

                if (res.Failed > 0 && res.Printed == 0)
                {
                    TempData["Error"] = $"Third-Party Appeal Application print failed. Failed: {res.Failed}.";
                    return RedirectToAction(nameof(Print), new { key });
                }

                /*
                 * TPA requires QA before Send Email.
                 * After printing/reprinting, create/recreate the QA sample automatically
                 * and send the user to the QA screen.
                 */
                if (await _qa.RequiresQaAsync(key, ct))
                {
                    var alreadyApproved = await _qa.IsQaApprovedAsync(key, ct);

                    if (!alreadyApproved)
                    {
                        var qaRunId = await _qa.CreateQaRunAsync(key, user, ct);

                        TempData["Success"] = forceReprint
                            ? $"Third-Party Appeal Application notices reprinted: {res.Printed}. QA sample #{qaRunId} created. Please approve QA before sending."
                            : $"Third-Party Appeal Application notices printed: {res.Printed}. QA sample #{qaRunId} created. Please approve QA before sending.";

                        return RedirectToAction(nameof(QA), new { key });
                    }
                }

                TempData["Success"] = forceReprint
                    ? $"Third-Party Appeal Application notices reprinted: {res.Printed}. " +
                      (res.Failed > 0 ? $"Failed: {res.Failed}." : "")
                    : $"Third-Party Appeal Application notices printed: {res.Printed}. " +
                      (res.Failed > 0 ? $"Failed: {res.Failed}." : "");

                return RedirectToAction(nameof(Print), new { key });
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
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var settings = await GetWorkflowSettingsAsync(key, ct);

            if (settings.Notice == NoticeKind.TPA)
            {
                return await PrintThirdPartyAppealApplications(key, false, ct);
            }

            var user = User?.Identity?.Name ?? "Unknown";
            var res = await _print.PrintAllBatchesAsync(key, user, ct);

            TempData["Success"] =
                $"All batches printed: {res.Printed} notices saved across {res.TotalBatches} batches. " +
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
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var settings = await GetWorkflowSettingsAsync(key, ct);

            if (settings.Notice == NoticeKind.TPA)
            {
                /*
                 * TPA does not use NoticeBatches.
                 * SendEmail.cshtml still expects Step3SendEmailVm as the main model.
                 * TPA-specific data must go through ViewBag.
                 */
                ViewBag.TpaEmail = await _tpaEmail.BuildEmailVmAsync(key, ct);

                var vm = await BuildTpaStep3SendEmailVmAsync(settings, key, ct);

                return View("SendEmail", vm);
            }

            var normalVm = await _printQuery.BuildEmailVmAsync(key, ct);
            return View("SendEmail", normalVm);
        }

        private async Task<Step3SendEmailVm> BuildTpaStep3SendEmailVmAsync(
    NoticeSettings settings,
    Guid key,
    CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == settings.RollId, ct);

            var totalReady = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .CountAsync(x =>
                    x.NoticeSettingsId == settings.Id &&
                    (x.Status == "Printed" || x.Status == "Email-Failed") &&
                    !string.IsNullOrWhiteSpace(x.PdfPath),
                    ct);

            var totalSent = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .CountAsync(x =>
                    x.NoticeSettingsId == settings.Id &&
                    x.Status == "Sent",
                    ct);

            return new Step3SendEmailVm
            {
                WorkflowKey = key,
                SettingsId = settings.Id,

                RollId = settings.RollId,
                RollShortCode = roll?.ShortCode ?? "GV23",
                RollName = roll?.Name ?? settings.RollName ?? "General Valuation Roll 2023",

                Notice = settings.Notice,
                Version = settings.Version,
                VersionText = $"V{settings.Version}",

                LetterDate = settings.LetterDate,
                ObjectionStartDate = settings.ObjectionStartDate,
                ObjectionEndDate = settings.ObjectionEndDate,
                ExtensionDate = settings.ExtensionDate,

                FinancialYearsText = settings.FinancialYearsText,

                IsS52 = false,
                S52IsReview = false,

                TotalBatches = 0,
                TotalPrinted = totalReady,
                TotalSent = totalSent,

                MaxEmailsPerSend = 999999,

                Batches = new()
            };
        }

        // POST: Send emails
        [HttpPost("SendEmail")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendEmail(
            Guid key,
            [FromForm(Name = "batchIds")] List<int> batchIds,
            CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var settings = await GetWorkflowSettingsAsync(key, ct);

            /*
             * TPA has no batchIds.
             * It sends directly from ThirdPartyAppealApplicationNotices
             * where Status = Printed / Email-Failed.
             */
            if (settings.Notice == NoticeKind.TPA)
            {
                var user = User?.Identity?.Name ?? "Unknown";

                try
                {
                    var res = await _tpaEmail.SendAsync(key, user, ct);

                    if (!string.IsNullOrWhiteSpace(res.ErrorMessage))
                    {
                        TempData["Error"] = res.ErrorMessage;
                        return RedirectToAction(nameof(SendEmail), new { key });
                    }

                    TempData["Success"] =
                        $"Third-Party Appeal Application emails sent: {res.Sent}. " +
                        (res.Failed > 0 ? $"Failed: {res.Failed}. " : "") +
                        (res.Skipped > 0 ? $"Skipped: {res.Skipped}." : "");

                    return RedirectToAction(nameof(SendStats), new { key });
                }
                catch (Exception ex)
                {
                    TempData["Error"] = ex.Message;
                    return RedirectToAction(nameof(SendEmail), new { key });
                }
            }

            if (batchIds == null || batchIds.Count == 0)
            {
                TempData["Error"] = "Please select at least one batch to send.";
                return RedirectToAction(nameof(SendEmail), new { key });
            }

            var normalUser = User?.Identity?.Name ?? "Unknown";
            var normalRes = await _emailSvc.SendBatchEmailsAsync(batchIds, key, normalUser, ct);

            if (!string.IsNullOrWhiteSpace(normalRes.ErrorMessage))
            {
                TempData["Error"] = normalRes.ErrorMessage;
            }
            else
            {
                TempData["Success"] =
                    $"Emails sent: {normalRes.Sent}. " +
                    (normalRes.Failed > 0 ? $"Failed: {normalRes.Failed}. " : "") +
                    (normalRes.Skipped > 0 ? $"No email address: {normalRes.Skipped}." : "");
            }

            return RedirectToAction(nameof(SendStats), new { key });
        }

        // ── PROGRESS ────────────────────────────────────────────────────────

        // GET: Live progress across ALL batches in a workflow
        [HttpGet("PrintProgressAll")]
        public async Task<IActionResult> PrintProgressAll(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var settings = await GetWorkflowSettingsAsync(key, ct);

            if (settings.Notice == NoticeKind.TPA)
            {
                var progress = await _tpaPrint.GetProgressAsync(key, ct);

                return Json(new
                {
                    total = progress.Total,
                    printed = progress.Printed,
                    sent = 0,
                    failed = progress.Failed,
                    generated = progress.Pending,
                    done = progress.Done
                });
            }

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
            var printed = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Printed)?.Count ?? 0;
            var sent = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Sent)?.Count ?? 0;
            var failed = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Failed)?.Count ?? 0;
            var generated = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Generated)?.Count ?? 0;
            var done = generated == 0 && total > 0;

            return Json(new { total, printed, sent, failed, generated, done });
        }

        // GET: Live progress for a batch being printed
        [HttpGet("PrintProgress")]
        public async Task<IActionResult> PrintProgress(int batchId, CancellationToken ct)
        {
            var counts = await _db.NoticeRunLogs
                .Where(r => r.NoticeBatchId == batchId)
                .GroupBy(r => r.Status)
                .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var total = counts.Sum(x => x.Count);
            var printed = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Printed)?.Count ?? 0;
            var sent = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Sent)?.Count ?? 0;
            var failed = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Failed)?.Count ?? 0;
            var generated = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Generated)?.Count ?? 0;

            var done = generated == 0 && (printed + sent + failed) >= total && total > 0;

            return Json(new { total, printed, sent, failed, generated, done });
        }

        // TPA print progress direct endpoint
        [HttpGet("ThirdPartyPrintProgress")]
        public async Task<IActionResult> ThirdPartyPrintProgress(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var progress = await _tpaPrint.GetProgressAsync(key, ct);

            return Json(new
            {
                total = progress.Total,
                pending = progress.Pending,
                printed = progress.Printed,
                failed = progress.Failed,
                done = progress.Done
            });
        }

        // AJAX: count records in selected batches
        [HttpPost("CountSelected")]
        public async Task<IActionResult> CountSelected(
            [FromForm(Name = "batchIds")] List<int> batchIds,
            CancellationToken ct)
        {
            var count = await _emailSvc.CountSelectedRecordsAsync(batchIds ?? new(), ct);
            return Json(new { count, over = count > 2000 });
        }

        // GET: Live email progress for a single batch
        [HttpGet("EmailProgress")]
        public async Task<IActionResult> EmailProgress(int batchId, CancellationToken ct)
        {
            var counts = await _db.NoticeRunLogs
                .Where(r => r.NoticeBatchId == batchId)
                .GroupBy(r => r.Status)
                .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var printed = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Printed)?.Count ?? 0;
            var sent = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Sent)?.Count ?? 0;
            var failed = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Failed)?.Count ?? 0;
            var noEmail = counts.FirstOrDefault(x => x.Status == (int)RunStatus.NoEmail)?.Count ?? 0;
            var total = counts.Sum(x => x.Count);
            var done = printed == 0 && (sent + failed + noEmail) >= total && total > 0;

            return Json(new { total, printed, sent, failed, noEmail, done });
        }

        // GET: Live email progress across ALL selected batches in a workflow
        [HttpGet("EmailProgressAll")]
        public async Task<IActionResult> EmailProgressAll(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var settings = await GetWorkflowSettingsAsync(key, ct);

            if (settings.Notice == NoticeKind.TPA)
            {
                var progress = await _tpaEmail.GetProgressAsync(key, ct);

                return Json(new
                {
                    total = progress.Total,
                    printed = progress.Pending,
                    sent = progress.Sent,
                    failed = progress.Failed,
                    noEmail = progress.Skipped,
                    done = progress.Done
                });
            }

            var batchIds = await _db.NoticeBatches
                .Where(b => b.WorkflowKey == key && b.BatchKind == "STEP3")
                .Select(b => b.Id)
                .ToListAsync(ct);

            if (batchIds.Count == 0)
                return Json(new { total = 0, printed = 0, sent = 0, failed = 0, noEmail = 0, done = true });

            var counts = await _db.NoticeRunLogs
                .Where(r => batchIds.Contains(r.NoticeBatchId))
                .GroupBy(r => r.Status)
                .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var printed = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Printed)?.Count ?? 0;
            var sent = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Sent)?.Count ?? 0;
            var failed = counts.FirstOrDefault(x => x.Status == (int)RunStatus.Failed)?.Count ?? 0;
            var noEmail = counts.FirstOrDefault(x => x.Status == (int)RunStatus.NoEmail)?.Count ?? 0;
            var total = counts.Sum(x => x.Count);
            var done = printed == 0 && total > 0;

            return Json(new { total, printed, sent, failed, noEmail, done });
        }

        // TPA email progress direct endpoint
        [HttpGet("ThirdPartyEmailProgress")]
        public async Task<IActionResult> ThirdPartyEmailProgress(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            var progress = await _tpaEmail.GetProgressAsync(key, ct);

            return Json(new
            {
                total = progress.Total,
                pending = progress.Pending,
                sent = progress.Sent,
                failed = progress.Failed,
                skipped = progress.Skipped,
                done = progress.Done
            });
        }

        // ── QA ───────────────────────────────────────────────────────────────

        // ── QA ───────────────────────────────────────────────────────────────

        [HttpGet("QA")]
        public async Task<IActionResult> QA(Guid key, CancellationToken ct)
        {
            if (key == Guid.Empty)
                return BadRequest("Invalid workflow key.");

            /*
             * Same QA view for all notices.
             * Normal notices use NoticeBatches / NoticeRunLogs inside the service.
             * TPA uses ThirdPartyAppealApplicationNotices inside the service.
             */
            var vm = await _qa.BuildQaVmAsync(key, ct);

            return View("QA", vm);
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
                var qaRunId = await _qa.CreateQaRunAsync(key, user, ct);

                TempData["Success"] = $"QA sample #{qaRunId} created successfully.";
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

                /*
                 * After QA approval, go straight to Send Email.
                 * This applies to normal notices and TPA.
                 */
                return RedirectToAction(nameof(SendEmail), new { key });
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(QA), new { key });
            }
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

        [HttpGet("OpenThirdPartyPdf")]
        public async Task<IActionResult> OpenThirdPartyPdf(int id, CancellationToken ct)
        {
            var notice = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (notice == null)
                return NotFound("Third-Party Appeal Application notice was not found.");

            if (string.IsNullOrWhiteSpace(notice.PdfPath))
                return NotFound("PDF path is empty.");

            if (!System.IO.File.Exists(notice.PdfPath))
                return NotFound("PDF file not found.");

            var fileName = Path.GetFileName(notice.PdfPath);
            var bytes = await System.IO.File.ReadAllBytesAsync(notice.PdfPath, ct);

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            return File(bytes, "application/pdf");
        }

        // ── STATS ───────────────────────────────────────────────────────────

        [HttpGet("SendStats")]
        public async Task<IActionResult> SendStats(
            Guid key,
            CancellationToken ct)
        {
            if (key == Guid.Empty)
            {
                return BadRequest("Invalid workflow key.");
            }

            try
            {
                /*
                 * NoticeSendStatsService keeps normal statistics and
                 * delegates TPA statistics to IThirdPartyAppealStatsService.
                 *
                 * Both notice types use:
                 * Views/Step3/SendStats.cshtml
                 */
                var vm = await _stats.BuildStatsAsync(
                    key,
                    ct);

                return View("SendStats", vm);
            }
            catch (Exception ex)
            {
                TempData["Error"] =
                    $"The statistics could not be loaded. {ex.Message}";

                return RedirectToAction(
                    nameof(SendEmail),
                    new { key });
            }
        }

        [HttpPost("SendAllTpaAdminReports")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendAllTpaAdminReports(
            Guid key,
            CancellationToken ct)
        {
            if (key == Guid.Empty)
            {
                return BadRequest("Invalid workflow key.");
            }

            var settings = await GetWorkflowSettingsAsync(
                key,
                ct);

            if (settings.Notice != NoticeKind.TPA)
            {
                return BadRequest(
                    "This action is only available for Third-Party Appeal statistics.");
            }

            var performedBy =
                User?.Identity?.Name ?? "Unknown";

            try
            {
                /*
                 * One button triggers the bulk operation.
                 * ThirdPartyAppealStatsService internally generates,
                 * archives and sends one separate workbook and .eml
                 * evidence file per eligible Admin.
                 */
                var result =
                    await _tpaStats.SendAllAdminReportsAsync(
                        key,
                        performedBy,
                        ct);

                TempData["Success"] =
                    $"TPA Admin reports completed. " +
                    $"Sent: {result.Sent}, " +
                    $"Failed: {result.Failed}, " +
                    $"Skipped: {result.Skipped}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] =
                    $"TPA Admin reports could not be sent. {ex.Message}";
            }

            return RedirectToAction(
                nameof(SendStats),
                new { key });
        }

        [HttpGet("DownloadTpaAdminExcel")]
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

            var settings = await GetWorkflowSettingsAsync(
                key,
                ct);

            if (settings.Notice != NoticeKind.TPA)
            {
                return BadRequest(
                    "This download is only available for Third-Party Appeal statistics.");
            }

            try
            {
                /*
                 * BuildAdminExcelAsync returns an in-memory workbook.
                 * Downloading does not create another evidence copy.
                 * Evidence is saved by SendAllAdminReportsAsync when sent.
                 */
                var excel =
                    await _tpaStats.BuildAdminExcelAsync(
                        key,
                        adminKey,
                        ct);

                return File(
                    excel.Content,
                    excel.ContentType,
                    excel.FileName);
            }
            catch (Exception ex)
            {
                TempData["Error"] =
                    $"The Admin workbook could not be generated. {ex.Message}";

                return RedirectToAction(
                    nameof(SendStats),
                    new { key });
            }
        }

        [HttpGet("DownloadStatsExcel")]
        public async Task<IActionResult> DownloadStatsExcel(
            Guid key,
            CancellationToken ct)
        {
            if (key == Guid.Empty)
            {
                return BadRequest("Invalid workflow key.");
            }

            var settings = await GetWorkflowSettingsAsync(
                key,
                ct);

            /*
             * TPA uses the per-Admin download links displayed directly
             * on Views/Step3/SendStats.cshtml.
             */
            if (settings.Notice == NoticeKind.TPA)
            {
                TempData["Error"] =
                    "Use the Download button next to the required Admin.";

                return RedirectToAction(
                    nameof(SendStats),
                    new { key });
            }

            var user =
                User?.Identity?.Name ?? "Unknown";

            try
            {
                var path =
                    await _stats.GenerateExcelAsync(
                        key,
                        user,
                        ct);

                if (!System.IO.File.Exists(path))
                {
                    return NotFound(
                        "The statistics workbook was not found.");
                }

                var bytes =
                    await System.IO.File.ReadAllBytesAsync(
                        path,
                        ct);

                return File(
                    bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                TempData["Error"] =
                    $"The statistics workbook could not be generated. {ex.Message}";

                return RedirectToAction(
                    nameof(SendStats),
                    new { key });
            }
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
            {
                return BadRequest("Invalid workflow key.");
            }

            var settings = await GetWorkflowSettingsAsync(
                key,
                ct);

            /*
             * TPA has its own single bulk button on the same SendStats view.
             * Do not redirect to StatsController.
             */
            if (settings.Notice == NoticeKind.TPA)
            {
                TempData["Error"] =
                    "Use Send All Admin Reports for Third-Party Appeal statistics.";

                return RedirectToAction(
                    nameof(SendStats),
                    new { key });
            }

            var user =
                User?.Identity?.Name ?? "Unknown";

            try
            {
                await _stats.SendStatsEmailAsync(
                    key,
                    toEmails,
                    ccEmails,
                    user,
                    ct);

                TempData["Success"] =
                    "Stats report sent to stakeholders successfully.";

                return RedirectToAction(
                    "Index",
                    "Home");
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;

                return RedirectToAction(
                    nameof(SendStats),
                    new { key });
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private async Task<NoticeSettings> GetWorkflowSettingsAsync(
            Guid key,
            CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ApprovalKey == key || x.WorkflowKey == key, ct);

            if (settings == null)
                throw new InvalidOperationException("Workflow settings were not found.");

            return settings;
        }
        [HttpGet("OpenQaPdfByPath")]
        public async Task<IActionResult> OpenQaPdfByPath(int qaItemId, CancellationToken ct)
        {
            var item = await _db.NoticeQaItems
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == qaItemId, ct);

            if (item == null)
                return NotFound("QA item not found.");

            if (string.IsNullOrWhiteSpace(item.PdfPath))
                return NotFound("PDF path is missing.");

            if (!System.IO.File.Exists(item.PdfPath))
                return NotFound("PDF file was not found on disk.");

            var bytes = await System.IO.File.ReadAllBytesAsync(item.PdfPath, ct);

            return File(bytes, "application/pdf");
        }
    }
}
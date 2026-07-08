using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Corrections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Controllers
{
    [Route("NoticeCorrection")]
    public class NoticeCorrectionController : Controller
    {
        private readonly AppDbContext _db;
        private readonly INoticeCorrectionSourceService _source;
        private readonly INoticeCorrectionBatchService _batch;
        private readonly INoticeCorrectionPrintService _print;
        private readonly INoticeCorrectionEmailService _correctionEmail;
        public NoticeCorrectionController(
    AppDbContext db,
    INoticeCorrectionSourceService source,
    INoticeCorrectionBatchService batch,
    INoticeCorrectionPrintService print,
    INoticeCorrectionEmailService correctionEmail)
        {
            _db = db;
            _source = source;
            _batch = batch;
            _print = print;
            _correctionEmail = correctionEmail;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var vm = await BuildSearchVmAsync(null, null, null, "", "", ct);
            return View(vm);
        }

        [HttpPost("Search")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Search(CorrectionSearchVm vm, CancellationToken ct)
        {
            if (vm.RollId == null || vm.RollId <= 0)
            {
                TempData["Error"] = "Please select a roll.";

                return View("Index", await BuildSearchVmAsync(
                    vm.RollId,
                    vm.SourceNotice,
                    vm.PrintNotice,
                    vm.ReferenceType,
                    vm.ReferenceNo,
                    ct));
            }

            if (vm.SourceNotice == null)
            {
                TempData["Error"] = "Please select the source notice where the corrected data must be pulled from.";

                return View("Index", await BuildSearchVmAsync(
                    vm.RollId,
                    vm.SourceNotice,
                    vm.PrintNotice,
                    vm.ReferenceType,
                    vm.ReferenceNo,
                    ct));
            }

            if (vm.PrintNotice == null)
            {
                TempData["Error"] = "Please select the print notice heading/template.";

                return View("Index", await BuildSearchVmAsync(
                    vm.RollId,
                    vm.SourceNotice,
                    vm.PrintNotice,
                    vm.ReferenceType,
                    vm.ReferenceNo,
                    ct));
            }

            if (string.IsNullOrWhiteSpace(vm.ReferenceType) || string.IsNullOrWhiteSpace(vm.ReferenceNo))
            {
                TempData["Error"] = "Please enter the reference type and reference number.";

                return View("Index", await BuildSearchVmAsync(
                    vm.RollId,
                    vm.SourceNotice,
                    vm.PrintNotice,
                    vm.ReferenceType,
                    vm.ReferenceNo,
                    ct));
            }

            try
            {
                var result = await _source.SearchAsync(
                    vm.RollId.Value,
                    vm.SourceNotice.Value,
                    vm.PrintNotice.Value,
                    vm.ReferenceType.Trim(),
                    vm.ReferenceNo.Trim(),
                    ct);

                if (result == null)
                {
                    TempData["Error"] = "No correction data found for the selected roll, source notice, print notice and reference.";

                    return View("Index", await BuildSearchVmAsync(
                        vm.RollId,
                        vm.SourceNotice,
                        vm.PrintNotice,
                        vm.ReferenceType,
                        vm.ReferenceNo,
                        ct));
                }

                return View("Preview", result);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;

                return View("Index", await BuildSearchVmAsync(
                    vm.RollId,
                    vm.SourceNotice,
                    vm.PrintNotice,
                    vm.ReferenceType,
                    vm.ReferenceNo,
                    ct));
            }
        }

        [HttpPost("CreateBatch")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBatch(CorrectionPreviewVm vm, CancellationToken ct)
        {
            try
            {
                var user = User?.Identity?.Name ?? "Unknown";

                var batchId = await _batch.CreateBatchAsync(vm, user, ct);

                TempData["Success"] = "Correction batch created successfully.";

                return RedirectToAction("PreviewBatch", new { id = batchId });
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return View("Preview", vm);
            }
        }

        [HttpGet("PreviewBatch/{id:int}")]
        public async Task<IActionResult> PreviewBatch(int id, CancellationToken ct)
        {
            var batch = await _db.NoticeCorrectionBatches
                .AsNoTracking()
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (batch == null)
                return NotFound();

            return View(batch);
        }

        private async Task<CorrectionSearchVm> BuildSearchVmAsync(
         int? selectedRollId,
         NoticeKind? selectedSourceNotice,
         NoticeKind? selectedPrintNotice,
         string referenceType,
         string referenceNo,
         CancellationToken ct)
        {
            var rolls = await _db.RollRegistry.AsNoTracking()
                .OrderBy(x => x.ShortCode)
                .ToListAsync(ct);

            return new CorrectionSearchVm
            {
                RollId = selectedRollId,

                SourceNotice = selectedSourceNotice,
                PrintNotice = selectedPrintNotice,

                ReferenceType = referenceType ?? "",
                ReferenceNo = referenceNo ?? "",

                Rolls = rolls.Select(r => new SelectListItem
                {
                    Value = r.RollId.ToString(),
                    Text = $"{r.ShortCode} — {r.Name}",
                    Selected = selectedRollId == r.RollId
                }).ToList(),

                SourceNoticeTypes = new List<SelectListItem>
        {
            new("Section 49", NoticeKind.S49.ToString(), selectedSourceNotice == NoticeKind.S49),
            new("Section 51", NoticeKind.S51.ToString(), selectedSourceNotice == NoticeKind.S51),
            new("Section 52", NoticeKind.S52.ToString(), selectedSourceNotice == NoticeKind.S52),
            new("Section 53 MVD", NoticeKind.S53.ToString(), selectedSourceNotice == NoticeKind.S53),
            new("Section 53 Revised MVD", NoticeKind.S53Rev.ToString(), selectedSourceNotice == NoticeKind.S53Rev),
            new("Dear Johnny", NoticeKind.DJ.ToString(), selectedSourceNotice == NoticeKind.DJ),
            new("Invalid Notice", NoticeKind.IN.ToString(), selectedSourceNotice == NoticeKind.IN)
        },

                PrintNoticeTypes = new List<SelectListItem>
        {
            new("Section 49", NoticeKind.S49.ToString(), selectedPrintNotice == NoticeKind.S49),
            new("Section 51", NoticeKind.S51.ToString(), selectedPrintNotice == NoticeKind.S51),
            new("Section 52", NoticeKind.S52.ToString(), selectedPrintNotice == NoticeKind.S52),
            new("Section 53 MVD", NoticeKind.S53.ToString(), selectedPrintNotice == NoticeKind.S53),
            new("Section 53 Revised MVD", NoticeKind.S53Rev.ToString(), selectedPrintNotice == NoticeKind.S53Rev),
            new("Dear Johnny", NoticeKind.DJ.ToString(), selectedPrintNotice == NoticeKind.DJ),
            new("Invalid Notice", NoticeKind.IN.ToString(), selectedPrintNotice == NoticeKind.IN)
        },

                ReferenceTypes = new List<SelectListItem>
        {
            new("Objection No", "Objection_No", referenceType == "Objection_No"),
            new("Appeal No", "Appeal_No", referenceType == "Appeal_No"),
            new("Premise ID", "PremiseId", referenceType == "PremiseId"),
            new("Valuation Key", "ValuationKey", referenceType == "ValuationKey"),
            new("Query No", "Query_No", referenceType == "Query_No"),
            new("Review No", "Review_No", referenceType == "Review_No")
        }
            };
        }

        [HttpPost("SaveBatchEmail")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveBatchEmail(
    CorrectionBatchEmailVm vm,
    CancellationToken ct)
        {
            if (vm.BatchId <= 0)
                return BadRequest("Invalid correction batch.");

            const string requiredCc = "ValuationEnquiries@joburg.org.za";

            var batch = await _db.NoticeCorrectionBatches
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == vm.BatchId, ct);

            if (batch == null)
                return NotFound();

            var cc = EnsureRequiredCc(vm.EmailCc, requiredCc);

            foreach (var item in batch.Items)
            {
                item.EmailSubject = vm.EmailSubject?.Trim();
                item.EmailBody = vm.EmailBody?.Trim();
                item.EmailCc = cc;
            }

            if (vm.SaveAsTemplate)
            {
                var templateName = string.IsNullOrWhiteSpace(vm.TemplateName)
                    ? $"{batch.NoticeKind} Correction Template"
                    : vm.TemplateName.Trim();

                var existingDefault = await _db.NoticeCorrectionEmailTemplates
                    .FirstOrDefaultAsync(x =>
                        x.NoticeKind == batch.NoticeKind &&
                        x.NoticeSubKind == batch.NoticeSubKind &&
                        x.IsDefault &&
                        x.IsActive,
                        ct);

                if (existingDefault == null)
                {
                    _db.NoticeCorrectionEmailTemplates.Add(new NoticeCorrectionEmailTemplate
                    {
                        NoticeKind = batch.NoticeKind,
                        NoticeSubKind = batch.NoticeSubKind,
                        TemplateName = templateName,
                        SubjectTemplate = vm.EmailSubject?.Trim() ?? "",
                        BodyTemplate = vm.EmailBody?.Trim() ?? "",
                        CcTemplate = cc,
                        IsDefault = true,
                        IsActive = true,
                        CreatedBy = User?.Identity?.Name ?? "Unknown",
                        CreatedAt = DateTime.Now
                    });
                }
                else
                {
                    existingDefault.TemplateName = templateName;
                    existingDefault.SubjectTemplate = vm.EmailSubject?.Trim() ?? "";
                    existingDefault.BodyTemplate = vm.EmailBody?.Trim() ?? "";
                    existingDefault.CcTemplate = cc;
                }
            }

            await _db.SaveChangesAsync(ct);

            TempData["Success"] = "Correction email wording saved successfully.";

            return RedirectToAction(nameof(PreviewBatch), new { id = vm.BatchId });
        }
        private static string EnsureRequiredCc(string? cc, string requiredCc)
        {
            var emails = (cc ?? "")
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (!emails.Any(x => string.Equals(x, requiredCc, StringComparison.OrdinalIgnoreCase)))
                emails.Add(requiredCc);

            return string.Join("; ", emails);
        }
        [HttpPost("PrintBatch/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrintBatch(int id, CancellationToken ct)
        {
            try
            {
                var user = User?.Identity?.Name ?? "Unknown";

                await _print.PrintBatchAsync(id, user, ct);

                TempData["Success"] = "Correction PDFs printed successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(PreviewBatch), new { id });
        }

        [HttpGet("QaReview/{id:int}")]
        public async Task<IActionResult> QaReview(int id, CancellationToken ct)
        {
            var batch = await _db.NoticeCorrectionBatches
                .AsNoTracking()
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (batch == null)
            {
                TempData["Error"] = "Correction batch was not found.";
                return RedirectToAction("SettingsLibrary", "Workflow");
            }

            if (batch.Items == null || !batch.Items.Any())
            {
                TempData["Error"] = "This correction batch has no items.";
                return RedirectToAction(nameof(PreviewBatch), new { id });
            }

            var notPrinted = batch.Items
                .Where(x => string.IsNullOrWhiteSpace(x.PdfPath))
                .ToList();

            if (notPrinted.Any())
            {
                TempData["Error"] = "QA cannot start because not all correction PDFs have been printed.";
                return RedirectToAction(nameof(PreviewBatch), new { id });
            }

            return View("QaReview", batch);
        }

        [HttpPost("ApproveQa/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveQa(
    int id,
    string? qaComment,
    CancellationToken ct)
        {
            var batch = await _db.NoticeCorrectionBatches
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (batch == null)
            {
                TempData["Error"] = "Correction batch was not found.";
                return RedirectToAction("SettingsLibrary", "Workflow");
            }

            if (batch.Items == null || !batch.Items.Any())
            {
                TempData["Error"] = "This correction batch has no items.";
                return RedirectToAction(nameof(PreviewBatch), new { id });
            }

            
            var missingPdf = batch.Items
    .Where(x => string.IsNullOrWhiteSpace(x.PdfPath) || !System.IO.File.Exists(x.PdfPath))
    .ToList();

            if (missingPdf.Any())
            {
                TempData["Error"] = "QA cannot be approved because one or more correction PDFs are missing from the file system.";
                return RedirectToAction(nameof(QaReview), new { id });
            }
            var user = User?.Identity?.Name ?? "Unknown";

            foreach (var item in batch.Items)
            {
                item.Status = "QA-Approved";
                item.ErrorMessage = null;

                // If you have QA columns, uncomment these after adding them.
                // item.QaApprovedBy = user;
                // item.QaApprovedAt = DateTime.Now;
                // item.QaComment = qaComment;
            }

            batch.Status = "QA-Approved";

            await _db.SaveChangesAsync(ct);

            TempData["Success"] = "Correction QA approved. You can now compose and send the correction email.";

            return RedirectToAction(nameof(PreviewBatch), new { id });
        }

        [HttpPost("RejectQa/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectQa(
    int id,
    string qaComment,
    CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(qaComment))
            {
                TempData["Error"] = "QA rejection comment is required.";
                return RedirectToAction(nameof(QaReview), new { id });
            }

            var batch = await _db.NoticeCorrectionBatches
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (batch == null)
            {
                TempData["Error"] = "Correction batch was not found.";
                return RedirectToAction("SettingsLibrary", "Workflow");
            }

            foreach (var item in batch.Items)
            {
                item.Status = "QA-Rejected";
                item.ErrorMessage = qaComment;

                // If you have QA columns, uncomment these after adding them.
                // item.QaApprovedBy = User?.Identity?.Name ?? "Unknown";
                // item.QaApprovedAt = DateTime.Now;
                // item.QaComment = qaComment;
            }

            batch.Status = "QA-Rejected";

            await _db.SaveChangesAsync(ct);

            TempData["Error"] = "Correction QA rejected.";

            return RedirectToAction(nameof(PreviewBatch), new { id });
        }

        [HttpGet("ViewCorrectionPdf/{itemId:int}")]
        public async Task<IActionResult> ViewCorrectionPdf(int itemId, CancellationToken ct)
        {
            var item = await _db.NoticeCorrectionItems
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == itemId, ct);

            if (item == null)
            {
                TempData["Error"] = "Correction item was not found.";
                return RedirectToAction("SettingsLibrary", "Workflow");
            }

            if (string.IsNullOrWhiteSpace(item.PdfPath) || !System.IO.File.Exists(item.PdfPath))
            {
                TempData["Error"] = "Correction PDF was not found.";
                return RedirectToAction(nameof(PreviewBatch), new { id = item.CorrectionBatchId });
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(item.PdfPath, ct);
            var fileName = Path.GetFileName(item.PdfPath);

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";

            return File(bytes, "application/pdf");
        }

        [HttpGet("DownloadCorrectionPdf/{itemId:int}")]
        public async Task<IActionResult> DownloadCorrectionPdf(int itemId, CancellationToken ct)
        {
            var item = await _db.NoticeCorrectionItems
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == itemId, ct);

            if (item == null)
            {
                TempData["Error"] = "Correction item was not found.";
                return RedirectToAction("SettingsLibrary", "Workflow");
            }

            if (string.IsNullOrWhiteSpace(item.PdfPath) || !System.IO.File.Exists(item.PdfPath))
            {
                TempData["Error"] = "Correction PDF was not found.";
                return RedirectToAction(nameof(PreviewBatch), new { id = item.CorrectionBatchId });
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(item.PdfPath, ct);
            var fileName = Path.GetFileName(item.PdfPath);

            return File(bytes, "application/pdf", fileName);
        }
        [HttpGet("ComposeCorrectionEmail/{id:int}")]
        public async Task<IActionResult> ComposeCorrectionEmail(int id, CancellationToken ct)
        {
            try
            {
                var vm = await _correctionEmail.BuildComposeVmAsync(id, ct);
                return View("ComposeCorrectionEmail", vm);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(PreviewBatch), new { id });
            }
        }

        [HttpPost("SendCorrectionEmail")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendCorrectionEmail(
    CorrectionEmailComposeVm vm,
    CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(vm.Subject))
                ModelState.AddModelError(nameof(vm.Subject), "Email subject is required.");

            if (string.IsNullOrWhiteSpace(vm.Body))
                ModelState.AddModelError(nameof(vm.Body), "Email body is required.");

            if (!ModelState.IsValid)
                return View("ComposeCorrectionEmail", vm);

            try
            {
                var user = User?.Identity?.Name ?? "Unknown";

                await _correctionEmail.SendBatchEmailAsync(vm, user, ct);

                TempData["Success"] = "Correction emails sent successfully.";

                // Return to Home Index after email send
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return View("ComposeCorrectionEmail", vm);
            }
        }
    }
}
using GV23_Notice.Data;
using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Helper;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services;
using GV23_Notice.Services.Audit;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.Notices;
using GV23_Notice.Services.Preview;
using GV23_Notice.Services.Preview.GV23_Notice.Services.Notices;
using GV23_Notice.Services.Rolls;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GV23_Notice.Controllers
{
    [Authorize]
    [Route("Workflow")]
    public class WorkflowController : Controller
    {
       
            private readonly AppDbContext _db;
            private readonly INoticeSettingsService _settings;
            private readonly IWorkflowAssetStorage _assets;
            private readonly IS53AppealCloseDateCalculator _s53Calc;
            private readonly ILogger<WorkflowController> _log;
             private readonly INoticePreviewService _preview;
        private readonly IWorkflowApprovalEmailService _wfEmails;
        private readonly INoticeEmailArchiveService _emailArchive;
        private readonly IOptions<EmailTemplateOptions> _emailOpt;
        private readonly IS49RollRepository _s49Roll;
        private readonly INoticeSettingsAuditService _audit;
        private readonly IPreviewDbDataService _previewRepo;
        private readonly ITempFileStore _tempFiles;

        public WorkflowController(
     AppDbContext db,
     INoticeSettingsService settings,
     IWorkflowAssetStorage assets,
     IS53AppealCloseDateCalculator s53Calc,
     ILogger<WorkflowController> log,
     INoticePreviewService previewService,
     IWorkflowApprovalEmailService wfEmails,
     INoticeEmailArchiveService emailArchiveService,
     IOptions<EmailTemplateOptions> emailOpt,
     IS49RollRepository s49Roll,
     INoticeSettingsAuditService audit,
     IPreviewDbDataService previewDb,
     ITempFileStore tempFiles)   // ✅ add
        {
            _db = db;
            _settings = settings;
            _assets = assets;
            _s53Calc = s53Calc;
            _log = log;

            _preview = previewService;
            _wfEmails = wfEmails;
            _emailArchive = emailArchiveService;
            _emailOpt = emailOpt;
            _s49Roll = s49Roll;
            _audit = audit;
            _previewRepo = previewDb;

            _tempFiles = tempFiles;     // ✅ assign
        }

        // GET: /Workflow/Step1
        [HttpGet("Step1")]
            public async Task<IActionResult> Step1(int? rollId, NoticeKind? notice, BatchMode? mode, int? settingsId, CancellationToken ct)
            {
                await PopulateRollsAsync(ct);
                        ViewBag.ValuationPeriods = Helper.ValuationPeriodCatalog.Periods
                .Select(p => new SelectListItem
                {
                    Value = p.Code,
                    Text = $"{p.Code} ({p.Start:dd MMM yyyy} – {p.End:dd MMM yyyy})"
                })
                .ToList();


            // If opening an existing draft/version
            if (settingsId.HasValue)
                {
                    var s = await _settings.GetByIdAsync(settingsId.Value, ct);
                    if (s is null) return NotFound();

                    var vm = MapToVm(s);
                    return View(vm);
                }

                // If user selected roll/notice/mode but no settingsId, load latest or create a draft
                if (rollId.HasValue && notice.HasValue && mode.HasValue)
                {
                    var latest = await _settings.GetLatestDraftOrApprovedAsync(rollId.Value, notice.Value, mode.Value, ct);
                    if (latest != null)
                    {
                        return View(MapToVm(latest));
                    }

                    // create draft automatically for smooth flow
                    var user = User?.Identity?.Name ?? "UNKNOWN";
                    var draft = await _settings.CreateDraftAsync(rollId.Value, notice.Value, mode.Value, user, ct);

                    return View(MapToVm(draft));
                }

                // Default empty vm
                return View(new WorkflowStep1Vm
                {
                    RollId = rollId ?? 0,
                    Notice = notice ?? NoticeKind.S49,
                    Mode = mode ?? BatchMode.Bulk,
                    LetterDate = DateTime.Today,
                    BatchDate = DateTime.Today
                });
            }

        private static void ApplyValuationAndFinancialYear(WorkflowStep1Vm vm, NoticeSettings e)
        {
            e.ValuationPeriodCode = vm.ValuationPeriodCode;

            var period = !string.IsNullOrWhiteSpace(vm.ValuationPeriodCode)
                ? Helper.ValuationPeriodCatalog.Get(vm.ValuationPeriodCode)
                : null;

            if (period != null)
            {
                e.ValuationPeriodStart = period.Start;
                e.ValuationPeriodEnd = period.End;
            }

            e.FinancialYearStart = vm.FinancialYearStart?.Date;
            e.FinancialYearEnd = vm.FinancialYearEnd?.Date;

            e.FinancialYearsText =
                (vm.FinancialYearStart.HasValue && vm.FinancialYearEnd.HasValue)
                    ? $"{vm.FinancialYearStart:dd MMMM yyyy} – {vm.FinancialYearEnd:dd MMMM yyyy}"
                    : null;
        }

        [HttpGet("FinancialYears")]
        public IActionResult FinancialYears(string valuationPeriodCode)
        {
            var period = Helper.ValuationPeriodCatalog.Get(valuationPeriodCode);
            if (period is null) return Ok(new List<object>());

            var years = Helper.ValuationPeriodCatalog.BuildFinancialYears(period)
                .Select(y => new
                {
                    start = y.start.ToString("yyyy-MM-dd"),
                    end = y.end.ToString("yyyy-MM-dd"),
                    text = y.text
                })
                .ToList();

            return Ok(years);
        }

        // POST: /Workflow/Step1 (Save Draft)  ✅ UPDATED: Save + Auto-Confirm + Redirect to Summary
        [HttpPost("Step1")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step1Save(
      WorkflowStep1Vm vm,
      IFormFile? signatureFile,
      IFormFile? overrideEvidenceFile,
      CancellationToken ct)
        {
            await PopulateRollsAsync(ct);
            var user = User?.Identity?.Name ?? "UNKNOWN";

            if (vm.RollId <= 0)
                ModelState.AddModelError(nameof(vm.RollId), "Roll is required.");

            // ✅ Valuation Period + Financial Year validation
            if (string.IsNullOrWhiteSpace(vm.ValuationPeriodCode))
                ModelState.AddModelError(nameof(vm.ValuationPeriodCode), "Valuation Period is required.");

            if (!vm.FinancialYearStart.HasValue || !vm.FinancialYearEnd.HasValue)
                ModelState.AddModelError(nameof(vm.FinancialYearStart), "Financial Year is required (1 July – 30 June).");

            // S49 override rule
            if (vm.Notice == NoticeKind.S49)
            {
                var today = DateTime.Today;
                vm.LetterDateOverridden = vm.LetterDate.Date != today;

                if (vm.LetterDateOverridden && string.IsNullOrWhiteSpace(vm.LetterDateOverrideReason))
                    ModelState.AddModelError(nameof(vm.LetterDateOverrideReason),
                        "Reason is required when Letter Date is overridden.");
            }

            // S51 close date
            if (vm.Notice == NoticeKind.S51)
                vm.EvidenceCloseDate = vm.LetterDate.Date.AddDays(30);

            // S53 appeal close date (unless override)
            if (vm.Notice == NoticeKind.S53)
            {
                vm.BatchDate ??= DateTime.Today;

                if (!vm.AppealCloseOverridden)
                {
                    var letter = DateOnly.FromDateTime(vm.LetterDate.Date);
                    var close = await _s53Calc.CalculateAsync(vm.RollId, letter, 45, ct);
                    vm.AppealCloseDate = close.ToDateTime(TimeOnly.MinValue);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(vm.AppealCloseOverrideReason))
                        ModelState.AddModelError(nameof(vm.AppealCloseOverrideReason),
                            "Reason is required when overriding Appeal Close Date.");

                    if ((overrideEvidenceFile is null || overrideEvidenceFile.Length <= 0) &&
                        string.IsNullOrWhiteSpace(vm.ExistingOverrideEvidencePath))
                        ModelState.AddModelError("overrideEvidenceFile",
                            "Evidence file is required when overriding Appeal Close Date.");

                    if (!vm.AppealCloseDate.HasValue)
                        ModelState.AddModelError(nameof(vm.AppealCloseDate),
                            "Appeal Close Date is required when overridden.");
                }
            }

            if (!ModelState.IsValid)
                return View("Step1", vm);

            // Create or load draft
            NoticeSettings entity;
            if (vm.SettingsId.HasValue)
            {
                entity = await _settings.GetByIdForUpdateAsync(vm.SettingsId.Value, ct) ?? (NoticeSettings)null!;
                if (entity is null) return NotFound();
            }
            else
            {
                entity = await _settings.CreateDraftAsync(vm.RollId, vm.Notice, vm.Mode, user, ct);
                vm.SettingsId = entity.Id;
            }

            // Save signature
            if (signatureFile is not null && signatureFile.Length > 0)
            {
                var path = await _assets.SaveSignatureAsync(vm.RollId, vm.Notice, entity.Version, signatureFile, ct);
                entity.SignaturePath = path;
                vm.ExistingSignaturePath = path;
            }

            // Save override evidence
            if (vm.Notice == NoticeKind.S53 && overrideEvidenceFile is not null && overrideEvidenceFile.Length > 0)
            {
                var path = await _assets.SaveOverrideEvidenceAsync(vm.RollId, vm.Notice, entity.Version, overrideEvidenceFile, ct);
                entity.AppealCloseOverrideEvidencePath = path;
                vm.ExistingOverrideEvidencePath = path;
            }

            // Map vm → entity (your existing mapping)
            ApplyVmToEntity(vm, entity);

            // ✅ apply new period fields
            ApplyValuationAndFinancialYear(vm, entity);

            await _settings.SaveDraftAsync(entity, ct);

            if (!entity.IsConfirmed)
                await _settings.ConfirmAsync(entity.Id, user, "Auto-confirmed after Save Draft.", ct);

            // ✅ AUDIT
            await _audit.WriteAsync(
                settingsId: entity.Id,
                step: "Step1",
                action: "SAVE_AND_CONFIRM",
                by: user,
                snapshot: new
                {
                    entity.Id,
                    entity.RollId,
                    entity.Notice,
                    entity.Mode,
                    entity.Version,
                    entity.LetterDate,
                    entity.ValuationPeriodCode,
                    entity.ValuationPeriodStart,
                    entity.ValuationPeriodEnd,
                    entity.FinancialYearStart,
                    entity.FinancialYearEnd,
                    entity.FinancialYearsText,
                    entity.SignaturePath
                },
                comment: null,
                ct: ct);

            TempData["Success"] = $"Saved and confirmed (v{entity.Version}). Please review summary to approve.";
            return RedirectToAction(nameof(Step1Summary), new { settingsId = entity.Id });
        }


        // GET: /Workflow/Step1Summary?settingsId=123
        [HttpGet("Step1Summary")]
        public async Task<IActionResult> Step1Summary(int settingsId, CancellationToken ct)
        {
            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            var roll = await _db.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);
            if (roll is null) return NotFound();

            var vm = new WorkflowStep1SummaryVm
            {
                SettingsId = s.Id,
                RollId = s.RollId,
                RollShortCode = roll.ShortCode,
                RollName = roll.Name,

                Notice = s.Notice,
                Mode = s.Mode,
                Version = s.Version,

                IsConfirmed = s.IsConfirmed,
                IsApproved = s.IsApproved,

                // common
                LetterDate = s.LetterDate,
                PortalUrl = s.PortalUrl,
                EnquiriesLine = s.EnquiriesLine,
                CityManagerSignDate = s.CityManagerSignDate,

                // s49
                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                SignaturePath = s.SignaturePath,

                // s51
                EvidenceCloseDate = s.EvidenceCloseDate,

                // s52
                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate,

                // s53
                BatchDate = s.BatchDate,
                AppealCloseDate = s.AppealCloseDate,
                AppealCloseOverrideReason = s.AppealCloseOverrideReason,
                AppealCloseOverrideEvidencePath = s.AppealCloseOverrideEvidencePath,

                // s78
                ExtractionDate = s.ExtractionDate,
                ExtractPeriodDays = s.ExtractPeriodDays,
                ReviewOpenDate = s.ReviewOpenDate,
                ReviewCloseDate = s.ReviewCloseDate
            };

            return View(vm);
        }


        // GET: /Workflow/SettingsLibrary?rollId=1&notice=S53&mode=Bulk
        [HttpGet("SettingsLibrary")]
        public async Task<IActionResult> SettingsLibrary(int? rollId, NoticeKind? notice, BatchMode? mode, CancellationToken ct)
        {
            var q = _db.NoticeSettings.AsNoTracking()
                .Where(x => x.IsConfirmed && x.IsApproved);

            if (rollId.HasValue) q = q.Where(x => x.RollId == rollId.Value);
            if (notice.HasValue) q = q.Where(x => x.Notice == notice.Value);
            if (mode.HasValue) q = q.Where(x => x.Mode == mode.Value);

            var list = await q
                .OrderByDescending(x => x.Id)
                .Take(200)
                .Select(x => new SettingsLibraryRowVm
                {
                    SettingsId = x.Id,
                    RollId = x.RollId,
                    Notice = x.Notice,
                    Mode = x.Mode,
                    Version = x.Version,
                    LetterDate = x.LetterDate,
                    IsConfirmed = x.IsConfirmed,
                    IsApproved = x.IsApproved,
                    SignaturePath = x.SignaturePath
                })
                .ToListAsync(ct);

            // add roll display
            var rollMap = await _db.RollRegistry.AsNoTracking()
                .ToDictionaryAsync(r => r.RollId, r => new { r.ShortCode, r.Name }, ct);

            foreach (var r in list)
            {
                if (rollMap.TryGetValue(r.RollId, out var rr))
                {
                    r.RollShortCode = rr.ShortCode;
                    r.RollName = rr.Name;
                }
            }

            return View(list);
        }

        // POST: /Workflow/Step1SummaryApprove
        [HttpPost("Step1SummaryApprove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step1SummaryApprove(int settingsId, CancellationToken ct)
        {
            var user = User?.Identity?.Name ?? "UNKNOWN";

            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            if (!s.IsConfirmed)
            {
                TempData["Error"] = "Settings must be confirmed before approval.";
                return RedirectToAction(nameof(Step1Summary), new { settingsId });
            }

            // ✅ S49 signature rule: allow upload before approval
            if (s.Notice == NoticeKind.S49 && string.IsNullOrWhiteSpace(s.SignaturePath))
            {
                TempData["Error"] = "Signature is required for Section 49. Please upload it on Step 1.";
                return RedirectToAction(nameof(Step1), new { settingsId = s.Id });
            }

            // ✅ S53 override evidence rule
            if (s.Notice == NoticeKind.S53 && !string.IsNullOrWhiteSpace(s.AppealCloseOverrideReason))
            {
                if (string.IsNullOrWhiteSpace(s.AppealCloseOverrideEvidencePath))
                {
                    TempData["Error"] = "Override evidence is required for Section 53 when overriding Appeal Close Date.";
                    return RedirectToAction(nameof(Step1), new { settingsId = s.Id });
                }
            }

            await _settings.ApproveAsync(s.Id, user, "Admin approved via Step1Summary.", ct);

            TempData["Success"] = "Approved. Redirecting to Step 2.";
            return RedirectToAction(nameof(Step2), new { settingsId = s.Id });
        }


        // POST: /Workflow/Step1Confirm
        [HttpPost("Step1Confirm")]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> Step1Confirm(WorkflowStep1Vm vm, CancellationToken ct)
            {
                if (!vm.SettingsId.HasValue) return BadRequest("SettingsId missing.");

                var user = User?.Identity?.Name ?? "UNKNOWN";
                await _settings.ConfirmAsync(vm.SettingsId.Value, user, "Admin confirmed Step1 settings.", ct);

                TempData["Success"] = "Settings confirmed.";
                return RedirectToAction(nameof(Step1), new { settingsId = vm.SettingsId.Value });
            }


        // ✅ inside WorkflowController (class-level helper)
        // ✅ inside WorkflowController (class-level helper)
        private static DataDomain ResolveDomain(NoticeKind notice)
        {
            // S52 is Appeal-domain, everything else is Objection-domain
            return notice == NoticeKind.S52 ? DataDomain.Appeal : DataDomain.Objection;
        }

        private static PreviewVariant ParseVariantOrDefault(string? variant)
        {
            if (string.IsNullOrWhiteSpace(variant))
                return PreviewVariant.Default;

            var v = variant.Trim();

            if (v.Equals("S52Review", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("S52ReviewDecision", StringComparison.OrdinalIgnoreCase))
                return PreviewVariant.S52ReviewDecision;

            if (v.Equals("S52Appeal", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("S52AppealDecision", StringComparison.OrdinalIgnoreCase))
                return PreviewVariant.S52AppealDecision;

            if (v.Equals("InvalidOmission", StringComparison.OrdinalIgnoreCase))
                return PreviewVariant.InvalidOmission;

            if (v.Equals("InvalidObjection", StringComparison.OrdinalIgnoreCase))
                return PreviewVariant.InvalidObjection;

            if (Enum.TryParse<PreviewVariant>(v, ignoreCase: true, out var parsed))
                return parsed;

            return PreviewVariant.Default;
        }



        // ✅ Step1Approve now ONLY validates + redirects to Step2 (NO approval/email here)
        [HttpPost("Step1Approve")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step1Approve(WorkflowStep1Vm vm, CancellationToken ct)
        {
            await PopulateRollsAsync(ct);

            if (!vm.SettingsId.HasValue)
            {
                TempData["Error"] = "Please Save Draft first, then Confirm, then proceed to Step 2.";
                return RedirectToAction(nameof(Step1), new { rollId = vm.RollId, notice = vm.Notice, mode = vm.Mode });
            }

            var s = await _settings.GetByIdAsync(vm.SettingsId.Value, ct);
            if (s is null) return NotFound();

            if (!s.IsConfirmed)
            {
                TempData["Error"] = "Please Confirm first before proceeding to Step 2.";
                return RedirectToAction(nameof(Step1), new { settingsId = s.Id });
            }

            // still enforce signature presence before Step2 (so Step2 shows correct state)
            if (s.Notice == NoticeKind.S49 && string.IsNullOrWhiteSpace(s.SignaturePath))
            {
                TempData["Error"] = "Signature is required for Section 49 before proceeding.";
                return RedirectToAction(nameof(Step1), new { settingsId = s.Id });
            }

            // S53 override evidence rule (you asked for it)
            if (s.Notice == NoticeKind.S53 && !string.IsNullOrWhiteSpace(s.AppealCloseOverrideReason))
            {
                if (string.IsNullOrWhiteSpace(s.AppealCloseOverrideEvidencePath))
                {
                    TempData["Error"] = "Override evidence is required for Section 53 before proceeding.";
                    return RedirectToAction(nameof(Step1), new { settingsId = s.Id });
                }
            }

            // ✅ Move to Step2 (preview + modals)
            return RedirectToAction(nameof(Step2), new { settingsId = s.Id, variant = "Default", mode = "single" });
        }


        // ✅ UPDATED Step1RequestCorrection (only changes: domain + SaveAsync signature)
        // ---------------------------
        // Helpers
        // ---------------------------

        private async Task PopulateRollsAsync(CancellationToken ct)
            {
                var rolls = await _db.RollRegistry
                    .AsNoTracking()
                    .Where(r => r.IsActive)
                    .OrderBy(r => r.RollId)
                    .Select(r => new SelectListItem
                    {
                        Value = r.RollId.ToString(),
                        Text = $"{r.ShortCode} - {r.Name}"
                    })
                    .ToListAsync(ct);

                ViewBag.Rolls = rolls;
            }

            private static WorkflowStep1Vm MapToVm(NoticeSettings s)
            {
                return new WorkflowStep1Vm
                {
                    SettingsId = s.Id,
                    RollId = s.RollId,
                    Notice = s.Notice,
                    Mode = s.Mode,

                    LetterDate = s.LetterDate,

                    LetterDateOverridden = s.LetterDateOverridden,
                    LetterDateOverrideReason = s.LetterDateOverrideReason,

                    PortalUrl = s.PortalUrl,
                    EnquiriesLine = s.EnquiriesLine,
                    CityManagerSignDate = s.CityManagerSignDate,

                    // S49
                    ObjectionStartDate = s.ObjectionStartDate,
                    ObjectionEndDate = s.ObjectionEndDate,
                    ExtensionDate = s.ExtensionDate,
                    ExistingSignaturePath = s.SignaturePath,

                    // S51
                    EvidenceCloseDate = s.EvidenceCloseDate,

                    // S52
                    BulkFromDate = s.BulkFromDate,
                    BulkToDate = s.BulkToDate,

                    // S53
                    BatchDate = s.BatchDate,
                    AppealCloseDate = s.AppealCloseDate,
                    AppealCloseOverridden = !string.IsNullOrWhiteSpace(s.AppealCloseOverrideReason),
                    AppealCloseOverrideReason = s.AppealCloseOverrideReason,
                    ExistingOverrideEvidencePath = s.AppealCloseOverrideEvidencePath,

                    // S78
                    ExtractionDate = s.ExtractionDate,
                    ExtractPeriodDays = s.ExtractPeriodDays,
                    ReviewOpenDate = s.ReviewOpenDate,
                    ReviewCloseDate = s.ReviewCloseDate
                };
            }

            private static void ApplyVmToEntity(WorkflowStep1Vm vm, NoticeSettings e)
            {
                e.RollId = vm.RollId;
                e.Notice = vm.Notice;
                e.Mode = vm.Mode;

                e.LetterDate = vm.LetterDate.Date;
                e.LetterDateOverridden = vm.LetterDateOverridden;
                e.LetterDateOverrideReason = vm.LetterDateOverridden ? vm.LetterDateOverrideReason : null;

                e.PortalUrl = vm.PortalUrl;
                e.EnquiriesLine = vm.EnquiriesLine;
                e.CityManagerSignDate = vm.CityManagerSignDate;

                // S49
                e.ObjectionStartDate = vm.ObjectionStartDate?.Date;
                e.ObjectionEndDate = vm.ObjectionEndDate?.Date;
                e.ExtensionDate = vm.ExtensionDate?.Date;

                // S51
                e.EvidenceCloseDate = vm.EvidenceCloseDate?.Date;

                // S52
                e.BulkFromDate = vm.BulkFromDate?.Date;
                e.BulkToDate = vm.BulkToDate?.Date;

                // S53
                e.BatchDate = vm.BatchDate?.Date;
                e.AppealCloseDate = vm.AppealCloseDate?.Date;

                if (vm.AppealCloseOverridden)
                {
                    e.AppealCloseOverrideReason = vm.AppealCloseOverrideReason;
                    e.AppealCloseOverrideBy = null; // optional: set to User in controller if you want
                    e.AppealCloseOverrideAtUtc = DateTime.UtcNow;
                }
                else
                {
                    e.AppealCloseOverrideReason = null;
                    e.AppealCloseOverrideBy = null;
                    e.AppealCloseOverrideAtUtc = null;
                    e.AppealCloseOverrideEvidencePath = null; // keep if you prefer, but safer to clear
                }

                // S78
                e.ExtractionDate = vm.ExtractionDate?.Date;
                e.ExtractPeriodDays = vm.ExtractPeriodDays;
                e.ReviewOpenDate = vm.ReviewOpenDate?.Date;
                e.ReviewCloseDate = vm.ReviewCloseDate?.Date;
            }

            [HttpGet("CalcS53AppealCloseDate")]
            public async Task<IActionResult> CalcS53AppealCloseDate(int rollId, DateTime letterDate, CancellationToken ct)
            {
                if (rollId <= 0) return BadRequest("rollId is required.");

                var close = await _s53Calc.CalculateAsync(
                    rollId,
                    DateOnly.FromDateTime(letterDate.Date),
                    45,
                    ct
                );

                // Return ISO for input[type=date]
                return Ok(new { appealCloseDate = close.ToString("yyyy-MM-dd") });
            }

        [HttpGet("Step2")]
        public async Task<IActionResult> Step2(
     int settingsId,
     string? variant,
     string? mode,
     string? appealNo,
     CancellationToken ct)
        {
            // 1) Snapshot (approved step1)
            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            if (!s.IsConfirmed)
            {
                TempData["Error"] = "Step 1 must be confirmed before Step 2.";
                return RedirectToAction(nameof(Step1), new { settingsId });
            }

            // 2) Parse UI -> enums
            var v = PreviewVariantParser.Parse(variant);
            var m = PreviewModeParser.Parse(mode);

            // ✅ Section 52 needs appealNo
            if (s.Notice == NoticeKind.S52 && string.IsNullOrWhiteSpace(appealNo))
            {
                TempData["Error"] = "Appeal No is required for Section 52 previews.";
                return RedirectToAction(nameof(Step1), new { settingsId });
            }

            // 3) Build preview (REAL DB row via stored procs inside service)
            var result = await _preview.BuildPreviewAsync(settingsId, v, m, appealNo, ct);

            // 4) Save pdf to temp and get URL for iframe
            var pdfFileName = string.IsNullOrWhiteSpace(result.PdfFileName)
                ? $"Preview_{result.RollShortCode}_{result.Notice}_{settingsId}.pdf"
                : result.PdfFileName;

            var pdfUrl = await _tempFiles.SavePdfAsync(result.PdfBytes, pdfFileName, ct);

            // 5) Roll info (for headers)
            var roll = await _db.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);

            // 6) Build VM
            var vm = new WorkflowStep2Vm
            {
                SettingsId = settingsId,
                RollId = s.RollId,
                RollShortCode = roll?.ShortCode ?? result.RollShortCode ?? "",
                RollName = roll?.Name ?? result.RollName ?? "",

                Notice = s.Notice,
                Mode = s.Mode,
                Version = s.Version,

                // recipient
                RecipientName = result.RecipientName ?? "",
                RecipientEmail = result.RecipientEmail ?? "",

                // email preview
                EmailSubject = result.EmailSubject ?? "",
                EmailBodyHtml = result.EmailBodyHtml ?? "",

                // pdf preview
                PdfUrl = pdfUrl,

                // keep UI selection
                SelectedVariant = (variant ?? "Default"),
                SelectedMode = (mode ?? "single"),
                AppealNo = appealNo, // ✅ add to vm if you have it

                // ✅ snapshot fields from settings (Step1)
                LetterDate = s.LetterDate,
                PortalUrl = s.PortalUrl,
                EnquiriesLine = s.EnquiriesLine,
                FinancialYearsText = s.FinancialYearsText,

                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,

                EvidenceCloseDate = s.EvidenceCloseDate,

                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate,

                BatchDate = s.BatchDate,
                AppealCloseDate = s.AppealCloseDate
            };

            return View(vm);
        }

        public static class PreviewVariantParser
        {
            public static PreviewVariant Parse(string? variant)
            {
                if (string.IsNullOrWhiteSpace(variant))
                    return PreviewVariant.Default;

                var v = variant.Trim();

                // Friendly aliases first
                if (v.Equals("S52Review", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("Review", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("S52ReviewDecision", StringComparison.OrdinalIgnoreCase))
                    return PreviewVariant.S52ReviewDecision;

                if (v.Equals("S52Appeal", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("Appeal", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("S52AppealDecision", StringComparison.OrdinalIgnoreCase))
                    return PreviewVariant.S52AppealDecision;

                if (v.Equals("InvalidOmission", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("Omission", StringComparison.OrdinalIgnoreCase))
                    return PreviewVariant.InvalidOmission;

                if (v.Equals("InvalidObjection", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("Objection", StringComparison.OrdinalIgnoreCase))
                    return PreviewVariant.InvalidObjection;

                // Try enum parse
                if (Enum.TryParse<PreviewVariant>(v, ignoreCase: true, out var parsed))
                    return parsed;

                return PreviewVariant.Default;
            }
        }
        public static class PreviewModeParser
        {
            public static PreviewMode Parse(string? mode)
            {
                if (string.IsNullOrWhiteSpace(mode))
                    return PreviewMode.Single;

                var m = mode.Trim().ToLowerInvariant();

                return m switch
                {
                    "single" => PreviewMode.Single,

                    // backward compat from older code
                    "multi" => PreviewMode.EmailMulti,

                    // new canonical
                    "emailmulti" => PreviewMode.EmailMulti,
                    "email-multi" => PreviewMode.EmailMulti,
                    "email_multi" => PreviewMode.EmailMulti,

                    // split / multipurpose
                    "split" => PreviewMode.SplitPdf,
                    "splitpdf" => PreviewMode.SplitPdf,
                    "split-pdf" => PreviewMode.SplitPdf,
                    "split_pdf" => PreviewMode.SplitPdf,

                    _ => PreviewMode.Single
                };
            }
        }


        [HttpGet("Step2Pdf")]
        public async Task<IActionResult> Step2Pdf(
       int settingsId,
       string? variant = null,
       string? mode = null,
       string? appealNo = null,
       CancellationToken ct = default)
        {
            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            var parsedVariant = ParseVariantOrDefault(variant);
            var parsedMode = ParseModeOrDefault(mode);

            if (s.Notice == NoticeKind.S52 && string.IsNullOrWhiteSpace(appealNo))
                return BadRequest("AppealNo is required for Section 52 previews.");

            var result = await _preview.BuildPreviewAsync(settingsId, parsedVariant, parsedMode, appealNo, ct);

            var fileName = string.IsNullOrWhiteSpace(result.PdfFileName)
                ? $"Preview_{result.RollShortCode}_{result.Notice}.pdf"
                : result.PdfFileName;

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            return File(result.PdfBytes, "application/pdf");
        }

        // ✅ Mode parsing: accept old values + new tabs
        private static PreviewMode ParseModeOrDefault(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return PreviewMode.Single;

            var m = mode.Trim().ToLowerInvariant();

            return m switch
            {
                "single" => PreviewMode.Single,

                // backward compat
                "multi" => PreviewMode.EmailMulti,

                // new canonical
                "emailmulti" => PreviewMode.EmailMulti,
                "email-multi" => PreviewMode.EmailMulti,

                "split" => PreviewMode.SplitPdf,
                "splitpdf" => PreviewMode.SplitPdf,
                "split-pdf" => PreviewMode.SplitPdf,

                _ => PreviewMode.Single
            };
        }

        // ✅ map enum -> stable querystring
        private static string ToUiMode(PreviewMode mode)
        {
            return mode switch
            {
                PreviewMode.EmailMulti => "emailmulti",
                PreviewMode.SplitPdf => "splitpdf",
                _ => "single"
            };
        }

        [HttpPost("Step2Approve")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2Approve(
     int settingsId,
     string? variant = null,
     string? mode = null,
     CancellationToken ct = default)
        {
            if (settingsId <= 0) return BadRequest("settingsId is required.");

            var user = User?.Identity?.Name ?? "UNKNOWN";

            // Load for validation
            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            if (!s.IsConfirmed)
            {
                TempData["Error"] = "Please Confirm Step 1 settings before approving in Step 2.";
                return RedirectToAction(nameof(Step1), new { settingsId });
            }

            // Required validations
            if (s.Notice == NoticeKind.S49 && string.IsNullOrWhiteSpace(s.SignaturePath))
            {
                TempData["Error"] = "Signature is required for Section 49 before approval.";
                return RedirectToAction(nameof(Step2), new { settingsId, variant, mode });
            }

            if (s.Notice == NoticeKind.S53 && !string.IsNullOrWhiteSpace(s.AppealCloseOverrideReason) &&
                string.IsNullOrWhiteSpace(s.AppealCloseOverrideEvidencePath))
            {
                TempData["Error"] = "Override evidence is required for Section 53 approval.";
                return RedirectToAction(nameof(Step2), new { settingsId, variant, mode });
            }

            // Approve + persist email archive + key
            await _settings.ApproveAsync(settingsId, user, "Data Team approved in Step2.", ct);

            var sUp = await _settings.GetByIdForUpdateAsync(settingsId, ct);
            if (sUp is null) return NotFound();

            var roll = await _db.RollRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.RollId == sUp.RollId, ct);
            if (roll is null) return NotFound();

            if (sUp.ApprovalKey == Guid.Empty)
                sUp.ApprovalKey = Guid.NewGuid();

            var baseUrl = (_emailOpt.Value.BaseUrl ?? "").Trim().TrimEnd('/');
            var kickoffUrl = $"{baseUrl}/Workflow/Step3Kickoff?settingsId={sUp.Id}&key={sUp.ApprovalKey:D}";

            var (subject, bodyHtml) = _wfEmails.BuildApprovalEmail(sUp, roll, user, kickoffUrl);

            var domain = ResolveDomain(sUp.Notice);

            var savedPath = await _emailArchive.SaveAsync(
                rollId: roll.RollId,
                domain: domain,
                rollShortCode: roll.ShortCode ?? "",
                notice: sUp.Notice.ToString(),
                version: sUp.Version,
                category: "Approval",
                fileStem: $"Approval_{roll.ShortCode}_{sUp.Notice}_{sUp.Mode}_SettingsId{sUp.Id}",
                subject: subject,
                bodyHtml: bodyHtml,
                meta: new
                {
                    Type = "Approval",
                    SettingsId = sUp.Id,
                    RollId = roll.RollId,
                    RollShortCode = roll.ShortCode,
                    Notice = sUp.Notice.ToString(),
                    Mode = sUp.Mode.ToString(),
                    Version = sUp.Version,
                    ApprovalKey = sUp.ApprovalKey,
                    KickoffUrl = kickoffUrl,
                    ApprovedBy = user,
                    ApprovedAtUtc = DateTime.UtcNow,

                    // snapshot of Step1
                    LetterDate = sUp.LetterDate,
                    ObjectionStartDate = sUp.ObjectionStartDate,
                    ObjectionEndDate = sUp.ObjectionEndDate,
                    ExtensionDate = sUp.ExtensionDate,
                    EvidenceCloseDate = sUp.EvidenceCloseDate,
                    BulkFromDate = sUp.BulkFromDate,
                    BulkToDate = sUp.BulkToDate,
                    BatchDate = sUp.BatchDate,
                    AppealCloseDate = sUp.AppealCloseDate,
                    SignaturePath = sUp.SignaturePath,
                    OverrideReason = sUp.AppealCloseOverrideReason,
                    OverrideEvidencePath = sUp.AppealCloseOverrideEvidencePath
                },
                ct: ct);

            sUp.ApprovedEmailSavedPath = savedPath;
            sUp.ApprovedEmailSentAtUtc = null;
            await _settings.SaveDraftAsync(sUp, ct);

            var parsedVariant = ParseVariantOrDefault(variant);
            var parsedMode = ParseModeOrDefault(mode);
            var uiMode = ToUiMode(parsedMode);

            TempData["Success"] = "Approved in Step 2. Approval email was generated and saved for Data Team handover.";
            return RedirectToAction(nameof(Step2), new { settingsId, variant = parsedVariant.ToString(), mode = uiMode });


       
        }

        // POST: /Workflow/Step2RequestCorrection
        [HttpPost("Step2RequestCorrection")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2RequestCorrection(
            int settingsId,
            string correctionComment,
            string? variant,
            string? mode,
            CancellationToken ct)
        {
            var user = User?.Identity?.Name ?? "UNKNOWN";
            var isMulti = string.Equals(mode?.Trim(), "multi", StringComparison.OrdinalIgnoreCase);
            var parsedVariant = ParseVariantOrDefault(variant);

            if (string.IsNullOrWhiteSpace(correctionComment))
            {
                TempData["Error"] = "Please provide a correction comment.";
                return RedirectToAction(nameof(Step2), new { settingsId, variant = parsedVariant.ToString(), mode = isMulti ? "multi" : "single" });
            }

            var s = await _settings.GetByIdForUpdateAsync(settingsId, ct);
            if (s is null) return NotFound();

            var roll = await _db.RollRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);
            if (roll is null) return NotFound();

            var (subject, bodyHtml) = _wfEmails.BuildCorrectionEmail(s, roll, user, correctionComment);

            var domain = ResolveDomain(s.Notice);

            var savedPath = await _emailArchive.SaveAsync(
                rollId: roll.RollId,
                domain: domain,
                rollShortCode: roll.ShortCode ?? "",
                notice: s.Notice.ToString(),
                version: s.Version,
                category: "Correction",
                fileStem: $"Correction_{roll.ShortCode}_{s.Notice}_{s.Mode}_SettingsId{s.Id}",
                subject: subject,
                bodyHtml: bodyHtml,
                meta: new
                {
                    Type = "Correction",
                    SettingsId = s.Id,
                    RollId = roll.RollId,
                    RollShortCode = roll.ShortCode,
                    Notice = s.Notice.ToString(),
                    Mode = s.Mode.ToString(),
                    Version = s.Version,
                    RequestedBy = user,
                    Reason = correctionComment,
                    RequestedAtUtc = DateTime.UtcNow,
                    Variant = parsedVariant.ToString(),
                    UiMode = isMulti ? "multi" : "single",
                    SignaturePath = s.SignaturePath
                },
                ct: ct);

            s.CorrectionEmailSavedPath = savedPath;
            s.CorrectionEmailSentAtUtc = null;
            await _settings.SaveDraftAsync(s, ct);

            var parsedMode = ParseModeOrDefault(mode);
            var uiMode = ToUiMode(parsedMode);
            TempData["Success"] = "Correction email generated and saved to folder.";

            return RedirectToAction(nameof(Step2), new { settingsId, variant = parsedVariant.ToString(), mode = uiMode });



           
           
        }


        [HttpGet("Step3Kickoff")]
        public async Task<IActionResult> Step3Kickoff(int settingsId, Guid key, CancellationToken ct)
        {
            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            if (!s.IsApproved)
                return BadRequest("Settings are not approved.");

            if (s.ApprovalKey == Guid.Empty || s.ApprovalKey != key)
                return Unauthorized("Invalid approval link.");

            var roll = await _db.RollRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);
            if (roll is null) return NotFound();

            // ✅ Real DB sample for Section49: pick first pending premiseId (TOP 1)
            string? samplePremise = null;
            string? sampleEmail = null;
            int sampleRowCount = 0;
            bool sampleIsSplit = false;

            if (s.Notice == NoticeKind.S49)
            {
                var picks = await _s49Roll.PickNextPremiseIdsAsync(s.RollId, top: 1, ct);
                samplePremise = picks.FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(samplePremise))
                {
                    var (rows, contact) = await _s49Roll.LoadPremiseAsync(s.RollId, samplePremise, ct);
                    sampleEmail = contact?.Email;
                    sampleRowCount = rows.Count;
                    sampleIsSplit = rows.Count > 1; // your rule: group by PremiseId having > 1
                }
            }

            // Build LIVE previews (PDF + Email)
            var previewMode = PreviewMode.Single; // kickoff always shows “single real preview”
            var previewVariant = PreviewVariant.Default;

            var pdfPreviewUrl = Url.Action(nameof(Step3PreviewPdf), "Workflow", new { settingsId, key }) ?? "";

            // Email preview: reuse your template service but with real contact/real property sample.
            // For now we can show a minimal read-only HTML block, then we’ll wire full template in Step3 service.
            var emailHtml = $@"
<div>
  <p><b>Sample Premise:</b> {samplePremise ?? "-"}</p>
  <p><b>Sample Email:</b> {sampleEmail ?? "-"}</p>
  <p>This is a Step 3 kickoff preview (live from DB). Sending happens when you start the batch.</p>
</div>";

            var vm = new GV23_Notice.Models.Workflow.ViewModels.WorkflowStep3KickoffVm
            {
                SettingsId = s.Id,
                ApprovalKey = s.ApprovalKey.Value,

                RollId = roll.RollId,
                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",

                Notice = s.Notice,
                Mode = s.Mode,
                Version = s.Version,

                IsApproved = s.IsApproved,
                ApprovedBy = s.ApprovedBy,         // ensure exists in NoticeSettings
                ApprovedAtUtc = s.ApprovedAtUtc,   // ensure exists in NoticeSettings

                LetterDate = s.LetterDate,
                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                FinancialYearsText = s.FinancialYearsText,

                SignaturePath = s.SignaturePath,

                SamplePremiseId = samplePremise,
                SampleEmail = sampleEmail,
                SampleRowCount = sampleRowCount,
                SampleIsSplit = sampleIsSplit,

                PdfPreviewUrl = pdfPreviewUrl,
                EmailPreviewHtml = emailHtml
            };

            return View(vm);
        }
        [HttpGet("Step3PreviewPdf")]
        public async Task<IActionResult> Step3PreviewPdf(int settingsId, Guid key, CancellationToken ct)
        {
            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            if (!s.IsApproved) return BadRequest("Not approved.");
            if (s.ApprovalKey == Guid.Empty || s.ApprovalKey != key) return Unauthorized("Invalid link.");

            if (s.Notice != NoticeKind.S49)
                return BadRequest("Preview not yet implemented for this notice in Step3.");

            var roll = await _db.RollRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);
            if (roll is null) return NotFound();

            // pick TOP 1 premise for preview
            var picks = await _s49Roll.PickNextPremiseIdsAsync(s.RollId, top: 1, ct);
            var premiseId = picks.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(premiseId))
                return BadRequest("No pending roll records found for preview.");

            var (rows, contact) = await _s49Roll.LoadPremiseAsync(s.RollId, premiseId, ct);
            if (contact is null || string.IsNullOrWhiteSpace(contact.Email))
                return BadRequest("No sapContacts email found for preview.");

            // Build PDF using REAL data:
            // If split exists, we must produce 4 rows:
            // Multipurpose(total), Business&Commercial, Residential, (blank if needed)
            var pdfBytes = BuildS49RealPreviewPdf(s, roll.ShortCode ?? "", rows, contact);

            var fileName = $"{roll.ShortCode}_S49_STEP3_PREVIEW_{premiseId}.pdf";
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(pdfBytes, "application/pdf");
        }

        private byte[] BuildS49RealPreviewPdf(
      NoticeSettings s,
      string rollShortCode,
      List<GV23_Notice.Models.DTOs.S49RollRowDto> rollRows,
      GV23_Notice.Models.DTOs.SapContactDto contact)
        {
            if (rollRows == null || rollRows.Count == 0)
                throw new InvalidOperationException("No roll rows found for S49 preview.");

            var headerPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images", "Obj_Header.PNG");

            var first = rollRows.First();

            // Decide if split/multipurpose
            var isSplit = rollRows.Count > 1 ||
                          rollRows.Any(r => !string.IsNullOrWhiteSpace(r.ValuationSplitIndicator));

            // Build rows for PDF table
            List<GV23_Notice.Services.Notices.Section49.Section49PropertyRow> propertyRows;

            if (isSplit)
            {
                // Use your rule: 4 rows always (Multipurpose + BC + Residential + blank)
                var splitRows = S49SplitHelper.Build4RowSplit(rollRows);

                propertyRows = splitRows.Select(x => new GV23_Notice.Services.Notices.Section49.Section49PropertyRow
                {
                    Category = x.Category ?? "",
                    MarketValue = x.MarketValue <= 0 ? "" : x.MarketValue.ToString("N0"),
                    Extent = x.Extent <= 0 ? "" : x.Extent.ToString("N0"),
                    Remarks = ""
                }).ToList();
            }
            else
            {
                // Single property
                propertyRows = new List<GV23_Notice.Services.Notices.Section49.Section49PropertyRow>
        {
            new GV23_Notice.Services.Notices.Section49.Section49PropertyRow
            {
                Category = first.CatDesc ?? "",
                MarketValue = first.MarketValue.ToString("N0"),
                Extent = first.Extent.ToString("N0"),
                Remarks = ""
            }
        };
            }

            // ✅ Build the NEW expected model (Section49PdfData)
            var data = new GV23_Notice.Services.Notices.Section49.Section49PdfData
            {
                Addr1 = contact.Addr1 ?? "Owner",
                Addr2 = contact.Addr2 ?? "",
                Addr3 = contact.Addr3 ?? "",
                Addr4 = contact.Addr4 ?? "",
                Addr5 = contact.Addr5 ?? "",

                PropertyDesc = first.PropertyDesc ?? "",
                LisStreetAddress = first.LisStreetAddress ?? contact.PremiseAddress ?? "",

                PremiseId = first.PremiseId ,   // use whichever exists in your DTO
                ValuationKey = first.ValuationKey ,

                // if you still display reason text anywhere
                Reason = first.Reason ?? "",

                PropertyRows = propertyRows,

                // If split -> force 4 rows rendering (your builder supports this)
                ForceFourRows = isSplit
            };

            // ✅ Use the correct context class name you implemented in the new builder
            var ctx = new GV23_Notice.Services.Notices.Section49.Section49NoticeContext
            {
                HeaderImagePath = headerPath,
                SignaturePath = s.SignaturePath ?? "",
                LetterDate = s.LetterDate,

                InspectionStartDate = s.ObjectionStartDate ?? s.LetterDate,
                InspectionEndDate = s.ObjectionEndDate ?? s.LetterDate.AddDays(30),
                ExtendedEndDate = s.ExtensionDate,

                FinancialYearsText = s.FinancialYearsText ?? "1 July 2025 – 30 June 2026",
                RollHeaderText = $"{rollShortCode} ROLL"
            };

            var builder = HttpContext.RequestServices
                .GetRequiredService<GV23_Notice.Services.Notices.Section49.ISection49PdfBuilder>();

            return builder.BuildNotice(data, ctx);
        }


        private static class S49SplitHelper
        {
            public sealed record Row(string Category, decimal MarketValue, decimal Extent);

            public static List<Row> Build4RowSplit(List<GV23_Notice.Models.DTOs.S49RollRowDto> rows)
            {
                // Normalise categories
                decimal SumValue(string contains) =>
                    rows.Where(r => (r.CatDesc ?? "").Contains(contains, StringComparison.OrdinalIgnoreCase))
                        .Sum(r => r.MarketValue);

                decimal SumExtent(string contains) =>
                    rows.Where(r => (r.CatDesc ?? "").Contains(contains, StringComparison.OrdinalIgnoreCase))
                        .Sum(r => r.Extent);

                // Business & Commercial
                var bcValue = rows.Where(r => (r.CatDesc ?? "").Contains("Business", StringComparison.OrdinalIgnoreCase)
                                           || (r.CatDesc ?? "").Contains("Commercial", StringComparison.OrdinalIgnoreCase))
                                  .Sum(r => r.MarketValue);

                var bcExtent = rows.Where(r => (r.CatDesc ?? "").Contains("Business", StringComparison.OrdinalIgnoreCase)
                                            || (r.CatDesc ?? "").Contains("Commercial", StringComparison.OrdinalIgnoreCase))
                                   .Sum(r => r.Extent);

                // Residential
                var resValue = rows.Where(r => (r.CatDesc ?? "").Contains("Residential", StringComparison.OrdinalIgnoreCase))
                                   .Sum(r => r.MarketValue);

                var resExtent = rows.Where(r => (r.CatDesc ?? "").Contains("Residential", StringComparison.OrdinalIgnoreCase))
                                    .Sum(r => r.Extent);

                // Multipurpose total = BC + Residential (your rule)
                var totalValue = bcValue + resValue;
                var totalExtent = bcExtent + resExtent;

                var result = new List<Row>
        {
            new Row("Multipurpose", totalValue, totalExtent),
            new Row("Business and Commercial", bcValue, bcExtent),
            new Row("Residential", resValue, resExtent),
            new Row("", 0, 0) // 4th row placeholder (keeps layout consistent)
        };

                return result;
            }
        }
        [HttpPost("Step3StartS49Batch")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step3StartS49Batch(int settingsId, Guid key, CancellationToken ct)
        {
            var user = User?.Identity?.Name ?? "UNKNOWN";

            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            if (!s.IsApproved) return BadRequest("Not approved.");
            if (s.ApprovalKey == Guid.Empty || s.ApprovalKey != key) return Unauthorized("Invalid link.");
            if (s.Notice != NoticeKind.S49) return BadRequest("This batch starter is for S49 only.");

            // Create next batch name like S49N_Batch_001
            var last = await _db.S49BatchRuns.AsNoTracking()
                .Where(x => x.SettingsId == settingsId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var nextNo = 1;
            if (last != null && last.BatchName.StartsWith("S49N_Batch_", StringComparison.OrdinalIgnoreCase))
            {
                var tail = last.BatchName.Replace("S49N_Batch_", "", StringComparison.OrdinalIgnoreCase);
                if (int.TryParse(tail, out var parsed)) nextNo = parsed + 1;
            }

            var batchName = $"S49N_Batch_{nextNo:000}";

            var premiseIds = await _s49Roll.PickNextPremiseIdsAsync(s.RollId, 500, ct);

            var run = new GV23_Notice.Domain.Workflow.Entities.S49BatchRun
            {
                SettingsId = s.Id,
                RollId = s.RollId,
                BatchName = batchName,
                TargetSize = 500,
                CreatedBy = user,
                PickedPremiseCount = premiseIds.Count,
                Status = "Created"
            };

            foreach (var pid in premiseIds)
            {
                var (rows, contact) = await _s49Roll.LoadPremiseAsync(s.RollId, pid, ct);

                run.Items.Add(new GV23_Notice.Domain.Workflow.Entities.S49BatchItem
                {
                    PremiseId = pid,
                    Email = contact?.Email,
                    Status = "Picked",
                    IsSplit = rows.Count > 1,
                    RowCount = rows.Count
                });
            }

            _db.S49BatchRuns.Add(run);
            await _db.SaveChangesAsync(ct);

            TempData["Success"] = $"Batch created: {batchName} ({premiseIds.Count} premiseIds). Next: sending runner.";

            // For now just return to kickoff.
            return RedirectToAction(nameof(Step3Kickoff), new { settingsId, key });
        }

    }

}

  




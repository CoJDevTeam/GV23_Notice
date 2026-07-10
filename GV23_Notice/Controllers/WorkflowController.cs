using GV23_Notice.Data;
using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Helper;
using GV23_Notice.Models.DTOs;
using GV23_Notice.Models.DTOs.GV23_Notice.Models.DTOs;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services;
using GV23_Notice.Services.Audit;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.Notices;
using GV23_Notice.Services.Preview;
using GV23_Notice.Services.Preview.GV23_Notice.Services.Notices;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.SnapShotStep2;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.ThirdPartyApplications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
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
        private readonly INoticeStep2SnapshotService _snap;
        private readonly GV23_Notice.Services.Step3.IS52RangePrintService _s52Range;
        private readonly IThirdPartyAppealDateConfigurationService _thirdPartyDates;
        private readonly IThirdPartyAppealWorkflowSyncService _tpaWorkflowSync;
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
     ITempFileStore tempFiles,
     INoticeStep2SnapshotService snap,
        IS52RangePrintService s52Range,
     IThirdPartyAppealDateConfigurationService thirdPartyDates,
     IThirdPartyAppealWorkflowSyncService tpaWorkflowSync)
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
            _snap = snap;
            _s52Range = s52Range;
            _thirdPartyDates = thirdPartyDates;
            _tempFiles = tempFiles;
            _tpaWorkflowSync = tpaWorkflowSync;
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
            var defaultVm = new WorkflowStep1Vm
            {
                RollId = rollId ?? 0,
                Notice = notice ?? NoticeKind.S49,
                Mode = mode ?? BatchMode.Bulk,
                LetterDate = DateTime.Today,
                BatchDate = DateTime.Today
            };

            if (defaultVm.Notice == NoticeKind.TPA)
            {
                var calc = await _thirdPartyDates.CalculateResponseDateAsync(
                    startDate: DateTime.Today,
                    responseDays: 51,
                    ct: ct);

                defaultVm.LetterDate = calc.StartDate;
            }
            return View(defaultVm);

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

            var isTpa = vm.Notice == NoticeKind.TPA;

            if (isTpa)
            {
                // TPA must not force the user to select a roll.
                // Remove any model validation error caused by RollId = 0.
                ModelState.Remove(nameof(vm.RollId));
            }
            else if (vm.RollId <= 0)
            {
                ModelState.AddModelError(nameof(vm.RollId), "Roll is required.");
            }
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
                    var close = await _s53Calc.CalculateAsync(vm.ValuationPeriodCode, letter, 45, ct);
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
            if (vm.Notice == NoticeKind.TPA)
            {
                /*
                 * TPA / Application to Valuation Appeal does not require the user
                 * to select a roll on Step 1.
                 *
                 * NoticeSettings still needs RollId internally, so we resolve it
                 * from the valuation period / default GV23 roll behind the scenes.
                 */
                if (!string.IsNullOrWhiteSpace(vm.ValuationPeriodCode))
                {
                    vm.RollId = await ResolveRollIdForTpaAsync(vm.ValuationPeriodCode, ct);
                    ModelState.Remove(nameof(vm.RollId));
                }

                /*
                 * Step 1 only shows an estimated response period.
                 * The final 51 days must still be recalculated on email send date.
                 * This uses SP: usp_ThirdPartyAppeal_CalculateResponseDueDate.
                 */
                var calc = await _thirdPartyDates.CalculateResponseDateAsync(
                    startDate: DateTime.Today,
                    responseDays: 51,
                    ct: ct);

                vm.LetterDate = calc.StartDate;
                vm.BatchDate = calc.StartDate;
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
            var rollCode = await ResolveRollCodeAsync(vm.RollId, ct);
            ApplyVmToEntity(vm, entity, rollCode);

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

            if (entity.Notice == NoticeKind.TPA)
            {
                var roll = await _db.RollRegistry
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.RollId == entity.RollId, ct);

                var tpaVm = await _thirdPartyDates.BuildAsync(
                    rollId: entity.RollId,
                    rollShortCode: roll?.ShortCode,
                    valuationPeriod: entity.ValuationPeriodCode ?? entity.FinancialYearsText ?? "GENERAL VALUATION ROLL 2023",
                    performedBy: user,
                    ct: ct);

                tpaVm.NoticeSettingsId = entity.Id;

                await _thirdPartyDates.SaveStep1AuditAsync(
                    tpaVm,
                    user,
                    ct);
            }
            TempData["Success"] = $"Saved and confirmed (v{entity.Version}). Please review summary to approve.";
            return RedirectToAction(nameof(Step1Summary), new { settingsId = entity.Id });
        }
        private async Task<int> ResolveRollIdForTpaAsync(
    string? valuationPeriodCode,
    CancellationToken ct)
        {
            /*
             * TPA is not selected by roll on Step 1.
             * But the existing workflow tables still require RollId.
             *
             * For now, map valuation period GV23 to active GV23 RollRegistry.
             * If valuation period changes later, extend this mapping.
             */
            var period = (valuationPeriodCode ?? "").Trim().ToUpperInvariant();

            var shortCode = period switch
            {
                "GV13" => "GV13",
                "GV18" => "GV18",
                "GV23" => "GV23",
                "GV27" => "GV27",
                _ => "GV23"
            };

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Where(x =>
                    x.ShortCode.ToUpper() == shortCode ||
                    x.ShortCode.ToUpper().Replace(" ", "") == shortCode.Replace(" ", ""))
                .OrderBy(x => x.RollId)
                .FirstOrDefaultAsync(ct);

            if (roll == null)
            {
                throw new InvalidOperationException(
                    $"No active RollRegistry record found for TPA using short code '{shortCode}'.");
            }

            return roll.RollId;
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

            ThirdPartyAppealResponseDateVm? tpaDate = null;

            if (s.Notice == NoticeKind.TPA)
            {
                tpaDate = await _thirdPartyDates.CalculateResponseDateAsync(
                    startDate: DateTime.Today,
                    responseDays: 51,
                    ct: ct);
            }
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

                LetterDate = s.LetterDate,
                PortalUrl = s.PortalUrl,
                EnquiriesLine = s.EnquiriesLine,
                CityManagerSignDate = s.CityManagerSignDate,

                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                SignaturePath = s.SignaturePath,

                EvidenceCloseDate = s.EvidenceCloseDate,

                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate,
                S52SendMode = s.S52SendMode,

                BatchDate = s.BatchDate,
                AppealCloseDate = s.AppealCloseDate,
                AppealCloseOverrideReason = s.AppealCloseOverrideReason,
                AppealCloseOverrideEvidencePath = s.AppealCloseOverrideEvidencePath,

                ExtractionDate = s.ExtractionDate,
                ExtractPeriodDays = s.ExtractPeriodDays,
                ReviewOpenDate = s.ReviewOpenDate,
                ReviewCloseDate = s.ReviewCloseDate,

                // TPA
                ThirdPartyResponseDays = tpaDate?.ResponseDays,
                ThirdPartyEstimatedSendDate = tpaDate?.StartDate,
                ThirdPartyEstimatedResponseDueDate = tpaDate?.ResponseDueDate
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
            var correctionBatches = await _db.NoticeCorrectionBatches
    .AsNoTracking()
    .Include(x => x.Items)
    .OrderByDescending(x => x.CreatedAt)
    .Take(100)
    .ToListAsync(ct);

            ViewBag.CorrectionBatches = correctionBatches;
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
            if (s.Notice == NoticeKind.TPA)
            {
                if (string.IsNullOrWhiteSpace(s.ValuationPeriodCode))
                {
                    TempData["Error"] = "Valuation period is required for Third-Party Appeal Application.";
                    return RedirectToAction(nameof(Step1), new { settingsId = s.Id });
                }

                var calc = await _thirdPartyDates.CalculateResponseDateAsync(
                    startDate: DateTime.Today,
                    responseDays: 51,
                    ct: ct);

                await _audit.WriteAsync(
                    settingsId: s.Id,
                    step: "Step1Summary",
                    action: "APPROVE_TPA_DATE_CONFIGURATION",
                    by: user,
                    snapshot: new
                    {
                        s.Id,
                        s.RollId,
                        s.Notice,
                        s.Mode,
                        s.Version,
                        s.ValuationPeriodCode,
                        EstimatedSendDate = calc.StartDate,
                        ResponseDays = calc.ResponseDays,
                        EstimatedResponseDueDate = calc.ResponseDueDate,
                        Rule = "Final 51 days starts from actual email sent date."
                    },
                    comment: "Third-Party Appeal Application configuration approved.",
                    ct: ct);
            }
            if (s.Notice == NoticeKind.TPA)
            {
                var updatedRows =
                    await _tpaWorkflowSync.SynchronizeAsync(
                        s.Id,
                        user,
                        ct);

                await _audit.WriteAsync(
                    settingsId: s.Id,
                    step: "Step1Summary",
                    action: "SYNC_TPA_ROLL_FROM_APPEAL_NUMBER",
                    by: user,
                    snapshot: new
                    {
                        s.Id,
                        UpdatedRows = updatedRows,
                        Rule =
                            "RollId, RollShortCode and ValuationPeriod resolved from Appeal_No using RollRegistry."
                    },
                    comment:
                        $"{updatedRows} TPA records were linked to the workflow and roll.",
                    ct: ct);
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
        private static DataDomain ResolveDomain(NoticeKind notice)
        {
            return notice == NoticeKind.S52 || notice == NoticeKind.TPA
                ? DataDomain.Appeal
                : DataDomain.Objection;
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
                S52SendMode = s.S52SendMode,

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

        private static void ApplyVmToEntity(WorkflowStep1Vm vm, NoticeSettings e, RollCode rollCode)
        {
            e.RollId = vm.RollId;
            e.Roll = rollCode;
            e.Notice = vm.Notice;
            e.Mode = vm.Mode;

            e.LetterDate = vm.LetterDate.Date;
            e.LetterDateOverridden = vm.LetterDateOverridden;
            e.LetterDateOverrideReason = vm.LetterDateOverridden ? vm.LetterDateOverrideReason : null;

            e.PortalUrl = vm.PortalUrl;
            e.EnquiriesLine = vm.EnquiriesLine;
            e.CityManagerSignDate = vm.CityManagerSignDate;

            e.ObjectionStartDate = vm.ObjectionStartDate?.Date;
            e.ObjectionEndDate = vm.ObjectionEndDate?.Date;
            e.ExtensionDate = vm.ExtensionDate?.Date;

            e.EvidenceCloseDate = vm.EvidenceCloseDate?.Date;

            e.BulkFromDate = vm.BulkFromDate?.Date;
            e.BulkToDate = vm.BulkToDate?.Date;

            // S52 send-mode (drives which sub-type to show Data Team)
            if (vm.Notice == NoticeKind.S52)
            {
                e.S52SendMode = vm.S52SendMode;
                // Keep legacy bool in sync for batch-naming and email template
                e.IsSection52Review = vm.S52SendMode == S52SendMode.ReviewOnly;
            }

            e.BatchDate = vm.BatchDate?.Date;
            e.AppealCloseDate = vm.AppealCloseDate?.Date;

            if (vm.AppealCloseOverridden)
            {
                e.AppealCloseOverrideReason = vm.AppealCloseOverrideReason;
                e.AppealCloseOverrideBy = null;
                e.AppealCloseOverrideAtUtc = DateTime.UtcNow;
            }
            else
            {
                e.AppealCloseOverrideReason = null;
                e.AppealCloseOverrideBy = null;
                e.AppealCloseOverrideAtUtc = null;
                e.AppealCloseOverrideEvidencePath = null;
            }

            e.ExtractionDate = vm.ExtractionDate?.Date;
            e.ExtractPeriodDays = vm.ExtractPeriodDays;
            e.ReviewOpenDate = vm.ReviewOpenDate?.Date;
            e.ReviewCloseDate = vm.ReviewCloseDate?.Date;
        }

        private async Task<RollCode> ResolveRollCodeAsync(int rollId, CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == rollId, ct);

            if (roll is null)
                throw new InvalidOperationException($"RollRegistry record not found for RollId {rollId}.");

            var code = (roll.ShortCode ?? "").Trim().ToUpperInvariant();

            return code switch
            {
                "GV23" => RollCode.GV23,
                "SUPP1" => RollCode.SUPP1,
                "SUPP 1" => RollCode.SUPP1,
                "SUPP2" => RollCode.SUPP2,
                "SUPP 2" => RollCode.SUPP2,
                "SUPP3" => RollCode.SUPP3,
                "SUPP 3" => RollCode.SUPP3,
                "QUERY" => RollCode.QUERY,
                _ => throw new InvalidOperationException($"Unsupported RollRegistry.ShortCode '{roll.ShortCode}'.")
            };
        }

        [HttpGet("CalcS53AppealCloseDate")]
        public async Task<IActionResult> CalcS53AppealCloseDate(
            string? valuationPeriodCode,
            DateTime letterDate,
            CancellationToken ct)
        {
            var close = await _s53Calc.CalculateAsync(
                valuationPeriodCode,
                DateOnly.FromDateTime(letterDate.Date),
                45,
                ct
            );

            // Return ISO string for input[type=date]
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
            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            if (!s.IsConfirmed)
            {
                TempData["Error"] = "Step 1 must be confirmed before Step 2.";
                return RedirectToAction(nameof(Step1), new { settingsId });
            }

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);

            if (roll is null)
            {
                return View(BuildStep2NotFoundVm(
                    s,
                    null,
                    variant,
                    mode,
                    appealNo,
                    "Roll Not Found",
                    "The roll linked to this notice setting could not be found.",
                    $"RollId {s.RollId} does not exist in RollRegistry."
                ));
            }

            var v = PreviewVariantParser.Parse(variant);
            var m = PreviewModeParser.Parse(mode);

            if (s.Notice == NoticeKind.S52 && string.IsNullOrWhiteSpace(variant))
            {
                variant = s.S52SendMode == S52SendMode.ReviewOnly ? "S52Review" : "S52Appeal";
                v = PreviewVariantParser.Parse(variant);
            }
            if (s.Notice == NoticeKind.TPA)
            {
                variant = "Default";
                mode = string.IsNullOrWhiteSpace(mode) ? "single" : mode;

                v = PreviewVariant.Default;
                m = PreviewMode.Single;
            }

            NoticePreviewResult result;

            try
            {
                result = await _preview.BuildPreviewAsync(settingsId, v, m, appealNo, ct);
            }
            catch (InvalidOperationException ex) when (IsPreviewNotFoundError(ex))
            {
                var vm = BuildStep2NotFoundVm(
                    s,
                    roll,
                    variant,
                    mode,
                    appealNo,
                    "Preview Not Found",
                    "No sample record was found for this preview. The setup is saved, but there is no matching data to display.",
                    ex.Message
                );

                return View(vm);
            }
            catch (SqlException ex) when (ex.Message.Contains("no data", StringComparison.OrdinalIgnoreCase))
            {
                var vm = BuildStep2NotFoundVm(
                    s,
                    roll,
                    variant,
                    mode,
                    appealNo,
                    "Preview Not Found",
                    "No preview data was returned from the database.",
                    ex.Message
                );

                return View(vm);
            }

            var pdfFileName = string.IsNullOrWhiteSpace(result.PdfFileName)
                ? $"Preview_{result.RollShortCode}_{result.Notice}_{settingsId}.pdf"
                : result.PdfFileName;

            var pdfUrl = await _tempFiles.SavePdfAsync(result.PdfBytes, pdfFileName, ct);

            var normalVm = new WorkflowStep2Vm
            {
                SettingsId = settingsId,
                RollId = s.RollId,
                RollShortCode = roll.ShortCode ?? result.RollShortCode ?? "",
                RollName = roll.Name ?? result.RollName ?? "",

                Notice = s.Notice,
                Mode = s.Mode,
                Version = s.Version,

                RecipientName = result.RecipientName ?? "",
                RecipientEmail = result.RecipientEmail ?? "",

                EmailSubject = result.EmailSubject ?? "",
                EmailBodyHtml = result.EmailBodyHtml ?? "",

                PdfUrl = pdfUrl,
                PdfPreviewUrl = pdfUrl,

                SelectedVariant = variant ?? "Default",
                SelectedMode = mode ?? "single",
                AppealNo = appealNo ?? "",

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
                S52SendMode = s.S52SendMode,

                BatchDate = s.BatchDate,
                AppealCloseDate = s.AppealCloseDate,

                SignaturePath = s.SignaturePath,
                IsConfirmed = s.IsConfirmed,
                IsApproved = s.IsApproved,

                PreviewFound = true
            };

            return View(normalVm);
        }

        private static bool IsPreviewNotFoundError(Exception ex)
        {
            var msg = ex.Message ?? "";

            return msg.Contains("no data", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("no roll row found", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("no roll rows found", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("no pending", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("missing PREMISEID", StringComparison.OrdinalIgnoreCase);
        }

        private static WorkflowStep2Vm BuildStep2NotFoundVm(
            NoticeSettings s,
            RollRegistry? roll,
            string? variant,
            string? mode,
            string? appealNo,
            string title,
            string message,
            string details)
        {
            return new WorkflowStep2Vm
            {
                SettingsId = s.Id,
                RollId = s.RollId,
                RollShortCode = roll?.ShortCode ?? "",
                RollName = roll?.Name ?? "",

                Notice = s.Notice,
                Mode = s.Mode,
                Version = s.Version,

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
                S52SendMode = s.S52SendMode,

                BatchDate = s.BatchDate,
                AppealCloseDate = s.AppealCloseDate,

                SignaturePath = s.SignaturePath,
                IsConfirmed = s.IsConfirmed,
                IsApproved = s.IsApproved,

                SelectedVariant = variant ?? "Default",
                SelectedMode = mode ?? "single",
                AppealNo = appealNo ?? "",

                RecipientName = "No preview record",
                RecipientEmail = "",
                EmailSubject = "",
                EmailBodyHtml = "",
                PdfUrl = "",
                PdfPreviewUrl = "",

                PreviewFound = false,
                PreviewNotFoundTitle = title,
                PreviewNotFoundMessage = message,
                PreviewNotFoundDetails = details
            };
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

            if (s.Notice == NoticeKind.TPA)
            {
                parsedVariant = PreviewVariant.Default;
                parsedMode = PreviewMode.Single;
            }

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


        private sealed record Recipients(string[] To, string[] Cc);

        private Recipients GetApprovalRecipients()
        {
            var opt = _emailOpt.Value;

            // Support both new structure and your older nested structure if it exists
            var to = (opt.ApprovalRecipients?.Length > 0 ? opt.ApprovalRecipients : Array.Empty<string>());
            var cc = (opt.ApprovalCcRecipients?.Length > 0 ? opt.ApprovalCcRecipients : Array.Empty<string>());

            return new Recipients(to, cc);
        }

        private Recipients GetCorrectionRecipients()
        {
            var opt = _emailOpt.Value;

            var to = (opt.CorrectionRecipients?.Length > 0 ? opt.CorrectionRecipients : Array.Empty<string>());
            var cc = (opt.CorrectionCcRecipients?.Length > 0 ? opt.CorrectionCcRecipients : Array.Empty<string>());

            return new Recipients(to, cc);
        }

        private async Task SendWorkflowEmailAsync(
            string subject,
            string bodyHtml,
            string[] to,
            string[] cc,
            CancellationToken ct)
        {
            var opt = _emailOpt.Value;

            // No recipients -> don't fail the workflow (but warn)
            if ((to == null || to.Length == 0) && (cc == null || cc.Length == 0))
                return;

            using var msg = new System.Net.Mail.MailMessage
            {
                From = new System.Net.Mail.MailAddress(
                    opt.FromAddress ?? "Propertyinfo@Joburg.org.za",
                    opt.FromName ?? "City of Johannesburg Valuation Services"),
                Subject = subject,
                Body = bodyHtml,
                IsBodyHtml = true
            };

            foreach (var addr in (to ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)))
                msg.To.Add(addr.Trim());

            foreach (var addr in (cc ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)))
                msg.CC.Add(addr.Trim());

            // ✅ if still empty, do nothing
            if (msg.To.Count == 0 && msg.CC.Count == 0)
                return;

            if (opt.Smtp == null || string.IsNullOrWhiteSpace(opt.Smtp.Host))
                throw new InvalidOperationException("Email:Smtp:Host is missing in appsettings.");

            using var smtp = new System.Net.Mail.SmtpClient(opt.Smtp.Host, opt.Smtp.Port)
            {
                EnableSsl = opt.Smtp.EnableSsl,
                Credentials = new System.Net.NetworkCredential(opt.Smtp.Username, opt.Smtp.Password)
            };

            // SmtpClient doesn't accept CancellationToken directly
            await Task.Run(() => smtp.Send(msg), ct);
        }
        // POST: /Workflow/Step2Approve
        [HttpPost("Step2Approve")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2Approve(Step2ApproveDto dto, CancellationToken ct)
        {
            var approvedBy = User?.Identity?.Name ?? "Unknown";

            var s = await _db.NoticeSettings.FirstOrDefaultAsync(x => x.Id == dto.SettingsId, ct);
            if (s is null) return NotFound();

            var roll = await _db.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);
            if (roll is null) return NotFound();

            if (!s.ApprovalKey.HasValue || s.ApprovalKey.Value == Guid.Empty)
            {
                s.ApprovalKey = Guid.NewGuid();
                await _db.SaveChangesAsync(ct);
            }

            var pv = await _preview.BuildPreviewAsync(dto.SettingsId, dto.Variant, dto.Mode, dto.AppealNo, ct);

            if (s.Notice == NoticeKind.TPA)
            {
                var calc = await _thirdPartyDates.CalculateResponseDateAsync(
                    startDate: DateTime.Today,
                    responseDays: 51,
                    ct: ct);

                await _audit.WriteAsync(
                    settingsId: s.Id,
                    step: "Step2",
                    action: "APPROVE_TPA_PREVIEW",
                    by: approvedBy,
                    snapshot: new
                    {
                        s.Id,
                        s.RollId,
                        s.Notice,
                        s.Mode,
                        s.Version,
                        s.ValuationPeriodCode,
                        EstimatedSendDate = calc.StartDate,
                        ResponseDays = calc.ResponseDays,
                        EstimatedResponseDueDate = calc.ResponseDueDate,
                        dto.Variant,
                       
                        dto.AppealNo,
                        PreviewFileName = pv.PdfFileName,
                        Rule = "Final 51 days starts from actual email sent date."
                    },
                    comment: "Third-Party Appeal Application preview approved.",
                    ct: ct);
            }

            await _snap.SaveApprovalAsync(
                settingsId: dto.SettingsId,
                variant: dto.Variant,
                mode: dto.Mode,
                approvedBy: approvedBy,
                emailSubject: pv.EmailSubject ?? "",
                emailBodyHtml: pv.EmailBodyHtml ?? "",
                pdfBytes: pv.PdfBytes ?? Array.Empty<byte>(),
                pdfFileName: pv.PdfFileName ?? "",
                ct: ct);

            var kickoffBaseUrl = Url.Action(
                action: nameof(Step3Kickoff),
                controller: "Workflow",
                values: new { settingsId = s.Id },
                protocol: Request.Scheme);

            if (string.IsNullOrWhiteSpace(kickoffBaseUrl))
                throw new InvalidOperationException("Could not build Step3Kickoff URL.");

            // S52: build separate kickoff URLs per sub-type so Data Team gets individual links
            string? appealKickoffUrl = null;
            string? reviewKickoffUrl = null;
            if (s.Notice == NoticeKind.S52)
            {
                var join = kickoffBaseUrl.Contains('?') ? "&" : "?";
                if (s.S52SendMode != S52SendMode.ReviewOnly)
                    appealKickoffUrl = $"{kickoffBaseUrl}{join}key={s.ApprovalKey:D}&variant=S52Appeal";
                if (s.S52SendMode != S52SendMode.AppealDecisionOnly)
                    reviewKickoffUrl = $"{kickoffBaseUrl}{join}key={s.ApprovalKey:D}&variant=S52Review";
            }

            var (approvalSubject, approvalBodyHtml) = _wfEmails.BuildApprovalEmail(
                s,
                roll,
                approvedBy,
                s.ApprovalKey.Value,
                kickoffBaseUrl,
                appealKickoffUrl,
                reviewKickoffUrl);

            var domain = ResolveDomain(s.Notice);

            var savedPath = await _emailArchive.SaveAsync(
                rollId: s.RollId,
                domain: domain,
                rollShortCode: roll.ShortCode ?? "",
                notice: s.Notice.ToString(),
                version: s.Version,
                category: "Approval",
                fileStem: $"Step2_{s.Notice}_Settings_{s.Id}",
                subject: approvalSubject,
                bodyHtml: approvalBodyHtml,
                meta: new
                {
                    s.Id,
                    s.RollId,
                    Roll = roll.ShortCode,
                    roll.Name,
                    Notice = s.Notice.ToString(),
                    Mode = s.Mode.ToString(),
                    Version = s.Version,
                    ApprovedBy = approvedBy,
                    ApprovedAtUtc = DateTime.UtcNow,
                    dto.Variant,
                    PreviewMode = dto.Mode,
                    ApprovalKey = s.ApprovalKey.Value
                },
                ct: ct);

            // ✅ SEND to Data Team + CC (from appsettings)
            try
            {
                var r = GetApprovalRecipients();
                await SendWorkflowEmailAsync(approvalSubject, approvalBodyHtml, r.To, r.Cc, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to send Step2 approval email for SettingsId={SettingsId}", s.Id);
                TempData["Warning"] = "Approved, but failed to send approval email. Check SMTP/appsettings.";
            }

            s.ApprovedEmailSavedPath = savedPath;
            s.ApprovedAtUtc = DateTime.UtcNow;
            s.ApprovedBy = approvedBy;
            await _db.SaveChangesAsync(ct);

            TempData["Success"] = "Step 2 approved — ready to create batches.";
            // Go directly to Step3Kickoff — fromStep2=true triggers the confirmation card
            return RedirectToAction(nameof(Step3Kickoff), new
            {
                settingsId = dto.SettingsId,
                key = s.ApprovalKey!.Value,
                variant = dto.Variant.ToString(),
                mode = ToUiMode(dto.Mode),
                fromStep2 = true
            });
        }

        [HttpPost("Step2RequestCorrection")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Step2RequestCorrection(Step2CorrectionDto dto, CancellationToken ct)
        {
            var requestedBy = User?.Identity?.Name ?? "Unknown";

            var s = await _db.NoticeSettings.FirstOrDefaultAsync(x => x.Id == dto.SettingsId, ct);
            if (s is null) return NotFound();

            var roll = await _db.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);
            if (roll is null) return NotFound();

            if (!s.ApprovalKey.HasValue || s.ApprovalKey.Value == Guid.Empty)
            {
                s.ApprovalKey = Guid.NewGuid();
                await _db.SaveChangesAsync(ct);
            }

            var pv = await _preview.BuildPreviewAsync(dto.SettingsId, dto.Variant, dto.Mode, dto.AppealNo, ct);

            await _snap.SaveCorrectionAsync(
                settingsId: dto.SettingsId,
                variant: dto.Variant,
                mode: dto.Mode,
                requestedBy: requestedBy,
                reason: dto.Reason ?? "",
                emailSubject: pv.EmailSubject ?? "",
                emailBodyHtml: pv.EmailBodyHtml ?? "",
                pdfBytes: pv.PdfBytes ?? Array.Empty<byte>(),
                pdfFileName: pv.PdfFileName ?? "",
                ct: ct);

            var step2Url = Url.Action(
                action: nameof(Step2),
                controller: "Workflow",
                values: new
                {
                    settingsId = s.Id,
                    variant = dto.Variant.ToString(),
                    mode = ToUiMode(dto.Mode),
                    appealNo = dto.AppealNo
                },
                protocol: Request.Scheme) ?? "";

            var (subject, bodyHtml) = _wfEmails.BuildCorrectionEmail(
                s,
                roll,
                requestedBy,
                dto.Reason ?? "",
                s.ApprovalKey.Value,
                step2Url);

            var domain = ResolveDomain(s.Notice);

            var savedPath = await _emailArchive.SaveAsync(
                rollId: s.RollId,
                domain: domain,
                rollShortCode: roll.ShortCode ?? "",
                notice: s.Notice.ToString(),
                version: s.Version,
                category: "Correction",
                fileStem: $"Step2Correction_{s.Notice}_Settings_{s.Id}",
                subject: subject,
                bodyHtml: bodyHtml,
                meta: new
                {
                    s.Id,
                    s.RollId,
                    Roll = roll.ShortCode,
                    roll.Name,
                    Notice = s.Notice.ToString(),
                    Mode = s.Mode.ToString(),
                    Version = s.Version,
                    RequestedBy = requestedBy,
                    RequestedAtUtc = DateTime.UtcNow,
                    Reason = dto.Reason ?? "",
                    dto.Variant,
                    PreviewMode = dto.Mode,
                    Step2Url = step2Url,
                    ApprovalKey = s.ApprovalKey.Value
                },
                ct: ct);

            // ✅ SEND to Data Team + CC (from appsettings)
            try
            {
                var r = GetCorrectionRecipients();
                await SendWorkflowEmailAsync(subject, bodyHtml, r.To, r.Cc, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to send Step2 correction email for SettingsId={SettingsId}", s.Id);
                TempData["Warning"] = "Correction saved, but failed to send correction email. Check SMTP/appsettings.";
            }

            s.CorrectionEmailSavedPath = savedPath;
            await _db.SaveChangesAsync(ct);

            TempData["Success"] = "Correction request saved with snapshot + email archived (and sent if configured).";
            return RedirectToAction("Step2", new
            {
                settingsId = dto.SettingsId,
                variant = dto.Variant.ToString(),
                mode = ToUiMode(dto.Mode),
                appealNo = dto.AppealNo
            });
        }

        [HttpGet("Step3Kickoff")]
        public async Task<IActionResult> Step3Kickoff(
            int settingsId,
            Guid key,
            string? variant,
            string? mode,
            string? appealNo,
            CancellationToken ct)
        {
            var s = await _settings.GetByIdAsync(settingsId, ct);
            if (s is null) return NotFound();

            if (!s.IsApproved)
                return BadRequest("Settings are not approved.");

            if (!s.ApprovalKey.HasValue || s.ApprovalKey.Value == Guid.Empty || s.ApprovalKey.Value != key)
                return Unauthorized("Invalid approval link.");

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct);

            if (roll is null) return NotFound();

            var shortCode = roll.ShortCode ?? "";

            var isS52 = s.Notice == NoticeKind.S52;
            var isTpa = s.Notice == NoticeKind.TPA;

            /*
             * Resolve preview variant and mode.
             * Normal notices use what came from the query string.
             * S52 has special review/appeal variants.
             * TPA always uses Default + Single because it has one formal owner notice preview.
             */
            var v = PreviewVariantParser.Parse(variant);
            var m = PreviewModeParser.Parse(mode);

            if (isS52 && string.IsNullOrWhiteSpace(variant))
            {
                variant = s.S52SendMode switch
                {
                    S52SendMode.ReviewOnly => "S52Review",
                    S52SendMode.AppealDecisionOnly => "S52Appeal",
                    _ => "S52Review"
                };

                v = PreviewVariantParser.Parse(variant);
            }

            if (isTpa)
            {
                variant = "Default";
                mode = "single";
                v = PreviewVariant.Default;
                m = PreviewMode.Single;
            }

            /*
             * S52 range count.
             * Only S52 uses the S52 range print service.
             */
            var s52RangeCount = 0;
            var s52IsReview = v == PreviewVariant.S52ReviewDecision;

            if (isS52)
            {
                try
                {
                    s52RangeCount = await _s52Range.CountRangeAsync(s.Id, s52IsReview, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "S52 range count failed for settingsId={SettingsId}", s.Id);
                }
            }

            /*
             * TPA response date.
             * This is still only an estimate at Step 3 Kickoff.
             * The final 51-day period must be recalculated again when the emails are sent.
             */
            ThirdPartyAppealResponseDateVm? tpaDate = null;

            if (isTpa)
            {
                tpaDate = await _thirdPartyDates.CalculateResponseDateAsync(
                    startDate: DateTime.Today,
                    responseDays: 51,
                    ct: ct);
            }

            /*
             * Build preview PDF using the normal preview service.
             * Next phase: make sure INoticePreviewService supports NoticeKind.TPA.
             */
            var result = await _preview.BuildPreviewAsync(settingsId, v, m, appealNo, ct);

            var pdfFileName = string.IsNullOrWhiteSpace(result.PdfFileName)
                ? $"Step3Kickoff_{result.RollShortCode}_{result.Notice}_{settingsId}.pdf"
                : result.PdfFileName;

            var pdfUrl = await _tempFiles.SavePdfAsync(result.PdfBytes, pdfFileName, ct);

            /*
             * Normal notices use NoticeBatches.
             * TPA does not create normal batches, but we still show Step 3 Kickoff.
             */
            var batchPrefix = ComputeBatchPrefix(s, shortCode);

            var batchesCreated = 0;
            var nextBatchCode = isTpa
                ? $"TPA_{shortCode.Replace(" ", "")}"
                : $"{batchPrefix}0001";

            var kickoffBatchRows = new List<KickoffBatchRowVm>();

            if (!isTpa)
            {
                batchesCreated = await _db.NoticeBatches
                    .AsNoTracking()
                    .CountAsync(b => b.WorkflowKey == key && b.BatchKind == "STEP3", ct);

                var createdBatchList = await _db.NoticeBatches
                    .AsNoTracking()
                    .Where(b => b.WorkflowKey == key && b.BatchKind == "STEP3")
                    .OrderByDescending(b => b.Id)
                    .Take(200)
                    .ToListAsync(ct);

                var lastBatch = createdBatchList
                    .FirstOrDefault(b => b.BatchName.StartsWith(batchPrefix, StringComparison.OrdinalIgnoreCase))
                    ?? await _db.NoticeBatches
                        .AsNoTracking()
                        .Where(b =>
                            b.RollId == s.RollId &&
                            b.Notice == s.Notice &&
                            b.BatchKind == "STEP3" &&
                            b.BatchName.StartsWith(batchPrefix))
                        .OrderByDescending(b => b.Id)
                        .FirstOrDefaultAsync(ct);

                var nextSeq = 1;

                if (lastBatch != null && lastBatch.BatchName.StartsWith(batchPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var tail = lastBatch.BatchName[batchPrefix.Length..];

                    if (int.TryParse(tail, out var parsed))
                        nextSeq = parsed + 1;
                }

                nextBatchCode = $"{batchPrefix}{nextSeq:0000}";

                kickoffBatchRows = createdBatchList
                    .Select(b => new KickoffBatchRowVm
                    {
                        BatchId = b.Id,
                        BatchName = b.BatchName,
                        BatchDate = b.BatchDate,
                        NumberOfRecords = b.NumberOfRecords,
                        CreatedBy = b.CreatedBy ?? "",
                        CreatedAtUtc = b.CreatedAtUtc,
                        IsApproved = b.IsApproved,
                        ApprovedBy = b.ApprovedBy,
                        ApprovedAtUtc = b.ApprovedAtUtc
                    })
                    .ToList();
            }

            var vm = new WorkflowStep3KickoffVm
            {
                SettingsId = s.Id,
                ApprovalKey = s.ApprovalKey.Value,

                RollId = roll.RollId,
                RollShortCode = shortCode,
                RollName = roll.Name ?? result.RollName ?? "",

                Notice = s.Notice,
                Mode = s.Mode,
                Version = s.Version,

                IsApproved = s.IsApproved,
                ApprovedBy = s.ApprovedBy,
                ApprovedAtUtc = s.ApprovedAtUtc,

                // Step 1 snapshot
                LetterDate = s.LetterDate,
                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                FinancialYearsText = s.FinancialYearsText,
                SignaturePath = s.SignaturePath,

                // Notice-specific fields
                EvidenceCloseDate = s.EvidenceCloseDate,
                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate,
                IsSection52Review = s.IsSection52Review,
                S53BatchDate = s.BatchDate,
                AppealCloseDate = s.AppealCloseDate,
                ExtractionDate = s.ExtractionDate,
                ExtractPeriodDays = s.ExtractPeriodDays,
                ReviewOpenDate = s.ReviewOpenDate,
                ReviewCloseDate = s.ReviewCloseDate,
                IsInvalidOmission = s.IsInvalidOmission,

                // Batch panel
                NextBatchCode = nextBatchCode,
                BatchesAlreadyCreated = batchesCreated,

                // Preview
                RecipientName = result.RecipientName ?? "",
                RecipientEmail = result.RecipientEmail ?? "",
                EmailSubject = result.EmailSubject ?? "",
                EmailBodyHtml = result.EmailBodyHtml ?? "",
                PdfUrl = pdfUrl,

                SelectedVariant = string.IsNullOrWhiteSpace(variant) ? "Default" : variant!,
                SelectedMode = string.IsNullOrWhiteSpace(mode) ? "single" : mode!,
                AppealNo = appealNo,

                // S52
                IsS52 = isS52,
                S52IsReview = s52IsReview,
                S52RangeCount = s52RangeCount,

                // TPA
                IsThirdPartyAppealApplication = isTpa,
                ThirdPartyResponseDays = tpaDate?.ResponseDays,
                ThirdPartyEstimatedSendDate = tpaDate?.StartDate,
                ThirdPartyEstimatedResponseDueDate = tpaDate?.ResponseDueDate,

                // Dashboard
                CreatedBatches = kickoffBatchRows,
                ShowBatchTab = !isTpa && TempData["Success"] != null
            };

            return View(vm);
        }
        private static string ComputeBatchPrefix(NoticeSettings s, string rollShortCode)
        {
            var code = rollShortCode.Replace(" ", "");

            return s.Notice switch
            {
                NoticeKind.S52 => s.IsSection52Review == true ? $"S52_{code}_" : $"AD_{code}_",
                NoticeKind.DJ => $"DJ_{code}_",
                NoticeKind.IN => s.IsInvalidOmission == true ? $"IOM_{code}_" : $"IOBJ_{code}_",
                NoticeKind.TPA => $"TPA_{code}_",
                _ => $"{s.Notice}_{code}_"
            };
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
      List<S49RollRowDto> rollRows,
     SapContactDto contact)
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

                PremiseId = first.PremiseId,   // use whichever exists in your DTO
                ValuationKey = first.ValuationKey,

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

            public static List<Row> Build4RowSplit(List<S49RollRowDto> rows)
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


        private async Task SendWorkflowEmailAsync(
    string subject,
    string bodyHtml,
    IEnumerable<string> to,
    IEnumerable<string> cc,
    CancellationToken ct)
        {
            var opt = _emailOpt.Value; // EmailTemplateOptions

            if (string.IsNullOrWhiteSpace(opt.Smtp.Host))
                throw new InvalidOperationException("Email:Smtp:Host is missing.");

            using var msg = new System.Net.Mail.MailMessage
            {
                From = new System.Net.Mail.MailAddress(opt.FromAddress, opt.FromName),
                Subject = subject,
                Body = bodyHtml,
                IsBodyHtml = true
            };

            foreach (var t in to.Where(x => !string.IsNullOrWhiteSpace(x)))
                msg.To.Add(t.Trim());

            foreach (var c in cc.Where(x => !string.IsNullOrWhiteSpace(x)))
                msg.CC.Add(c.Trim());

            using var smtp = new System.Net.Mail.SmtpClient(opt.Smtp.Host, opt.Smtp.Port)
            {
                EnableSsl = opt.Smtp.EnableSsl,
                Credentials = new System.Net.NetworkCredential(opt.Smtp.Username, opt.Smtp.Password)
            };

            // SmtpClient has no SendAsync(ct) in a nice way; use Task.Run to honour ct lightly
            await Task.Run(() => smtp.Send(msg), ct);
        }

    }

}
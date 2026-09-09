using GV23_Notice.Data;
using GV23_Notice.Domain.Section49;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Workflow;
using GV23_Notice.Services.Rolls;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;

namespace GV23_Notice.Services.QA
{
    public sealed class NoticeQaService : INoticeQaService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly INoticeSourceStatusService _sourceStatus;
        private readonly Section49Options _section49;
        private readonly RollDbOptions _rollDb;
        private readonly IRollDbConnectionFactory _rollConnectionFactory;

        private const int QaTargetTotal = 10;
        private const int QaMaxPerGroup = 3;

        public NoticeQaService(
            AppDbContext db,
            IConfiguration config,
            INoticeSourceStatusService sourceStatus,
            IOptions<Section49Options> section49Options,
            IOptions<RollDbOptions> rollDbOptions,
            IRollDbConnectionFactory rollConnectionFactory)
        {
            _db = db;
            _config = config;
            _sourceStatus = sourceStatus;
            _section49 = section49Options.Value;
            _rollDb = rollDbOptions.Value;
            _rollConnectionFactory = rollConnectionFactory;
        }
        public async Task<bool> RequiresQaAsync(
     Guid workflowKey,
     CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ApprovalKey == workflowKey ||
                         x.WorkflowKey == workflowKey,
                    ct);

            if (settings == null)
                return false;

            return settings.Notice switch
            {
                // Section 49 QA is configuration driven.
                NoticeKind.S49 => _section49.Qa.Enabled,

                NoticeKind.S51 => true,
                NoticeKind.S52 => true,
                NoticeKind.S53 => true,
                NoticeKind.S53Rev => true,
                NoticeKind.DJ => true,
                NoticeKind.IN => true,
                NoticeKind.S78 => true,
                NoticeKind.TPA => true,
                NoticeKind.CLA_TPA => true,

                _ => false
            };
        }

        public async Task<bool> IsQaApprovedAsync(Guid workflowKey, CancellationToken ct)
        {
            if (!await RequiresQaAsync(workflowKey, ct))
                return true;

            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ApprovalKey == workflowKey ||
                         x.WorkflowKey == workflowKey,
                    ct);

            if (settings?.Notice == NoticeKind.S49)
            {
                return await IsS49QaApprovedAsync(
                    workflowKey,
                    ct);
            }

            return await _db.NoticeQaRuns
                .AsNoTracking()
                .AnyAsync(x =>
                    x.WorkflowKey == workflowKey &&
                    x.Status == "Approved", ct);
        }

        public async Task<NoticeQaVm> BuildQaVmAsync(Guid workflowKey, CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey || x.WorkflowKey == workflowKey, ct)
                ?? throw new InvalidOperationException("Workflow settings not found.");

            if (settings.Notice == NoticeKind.TPA)
            {
                return await BuildTpaQaVmAsync(
                    workflowKey,
                    settings,
                    ct);
            }

            if (settings.Notice == NoticeKind.CLA_TPA)
            {
                return await BuildClaQaVmAsync(
                    workflowKey,
                    settings,
                    ct);
            }

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == settings.RollId, ct)
                ?? throw new InvalidOperationException("Roll not found.");

            var batchIds = await _db.NoticeBatches
                .AsNoTracking()
                .Where(x => x.WorkflowKey == workflowKey)
                .Select(x => x.Id)
                .ToListAsync(ct);

            var totalPrinted = await _db.NoticeRunLogs
                .AsNoTracking()
                .CountAsync(x =>
                    batchIds.Contains(x.NoticeBatchId) &&
                    x.Status == RunStatus.Printed &&
                    x.PdfPath != null &&
                    x.PdfPath != "", ct);

            var qaRun = await _db.NoticeQaRuns
                .AsNoTracking()
                .Include(x => x.Items)
                .Where(x => x.WorkflowKey == workflowKey)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var vm = new NoticeQaVm
            {
                WorkflowKey = workflowKey,
                SettingsId = settings.Id,
                RollId = settings.RollId,
                RollShortCode = roll.ShortCode,
                RollName = roll.Name,
                Notice = settings.Notice,
                VersionText = $"V{settings.Version}",
                TotalPrinted = totalPrinted,
                QaStatus = qaRun?.Status ?? "NotStarted",
                QaRunId = qaRun?.Id,
                IsApproved = qaRun?.Status == "Approved"
            };

            if (qaRun == null)
                return vm;

            var items = qaRun.Items
                .OrderBy(x => x.PropertyType)
                .ThenBy(x => x.ObjectionNo)
                .Select(x => new NoticeQaItemVm
                {
                    QaItemId = x.Id,
                    NoticeRunLogId = x.NoticeRunLogId ?? 0,
                    ObjectionNo = x.ObjectionNo,
                    PremiseId = x.PremiseId,
                    PropertyType = x.PropertyType,
                    PropertyDesc = x.PropertyDesc,
                    PdfPath = x.PdfPath,
                    NewCategoryMvd = x.NewCategoryMvd,
                    New2CategoryMvd = x.New2CategoryMvd,
                    New3CategoryMvd = x.New3CategoryMvd,
                    ExpectedCategory = x.ExpectedCategory,
                    IsCategoryValid = x.IsCategoryValid,
                    QaStatus = x.QaStatus,
                    QaComment = x.QaComment
                })
                .ToList();

            vm.TotalQaItems = items.Count;
            vm.FailedItems = items.Count(x => !x.IsCategoryValid || x.QaStatus == "Failed");

            vm.CanApprove =
                items.Count > 0 &&
                items.All(x => x.IsCategoryValid) &&
                qaRun.Status != "Approved";

            var groupLabel = DetermineQaGroupLabel(settings.Notice);

            vm.Groups = items
                .GroupBy(x => string.IsNullOrWhiteSpace(x.PropertyType) ? "General" : x.PropertyType)
                .Select(g => new NoticeQaGroupVm
                {
                    PropertyType = g.Key,
                    GroupLabel = groupLabel,
                    Items = g.ToList()
                })
                .ToList();

            return vm;
        }

        public async Task<int> CreateQaRunAsync(Guid workflowKey, string user, CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey || x.WorkflowKey == workflowKey, ct)
                ?? throw new InvalidOperationException("Workflow settings not found.");

            if (!await RequiresQaAsync(workflowKey, ct))
                throw new InvalidOperationException("This notice type does not require this QA step.");

            if (settings.Notice == NoticeKind.S49)
            {
                return await CreateS49QaRunAsync(
                    workflowKey,
                    settings,
                    user,
                    ct);
            }

            if (settings.Notice == NoticeKind.TPA)
            {
                return await CreateTpaQaRunAsync(
                    workflowKey,
                    settings,
                    user,
                    ct);
            }

            if (settings.Notice == NoticeKind.CLA_TPA)
            {
                return await CreateClaQaRunAsync(
                    workflowKey,
                    settings,
                    user,
                    ct);
            }

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == settings.RollId, ct)
                ?? throw new InvalidOperationException("Roll not found.");

            var oldOpenRuns = await _db.NoticeQaRuns
                .Where(x => x.WorkflowKey == workflowKey && x.Status == "Open")
                .ToListAsync(ct);

            foreach (var old in oldOpenRuns)
                old.Status = "Replaced";

            var batchIds = await _db.NoticeBatches
                .AsNoTracking()
                .Where(x => x.WorkflowKey == workflowKey)
                .Select(x => x.Id)
                .ToListAsync(ct);

            if (batchIds.Count == 0)
                throw new InvalidOperationException("No batches found for this workflow.");

            var printedLogs = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x =>
                    batchIds.Contains(x.NoticeBatchId) &&
                    x.Status == RunStatus.Printed &&
                    x.PdfPath != null &&
                    x.PdfPath != "" &&
                    x.ObjectionNo != null &&
                    x.ObjectionNo != "")
                .OrderBy(x => x.ObjectionNo)
                .Select(x => new PrintedLogLite
                {
                    NoticeRunLogId = x.Id,
                    ObjectionNo = x.ObjectionNo!,
                    PremiseId = x.PremiseId,
                    PropertyDesc = x.PropertyDesc,
                    PdfPath = x.PdfPath
                })
                .ToListAsync(ct);

            if (printedLogs.Count == 0)
                throw new InvalidOperationException("No printed notices found for QA. Print the batch first.");

            // A notice may have more than one Printed run log after a reprint.
            // QA must contain each objection only once, using the latest printed run.
            printedLogs = printedLogs
                .GroupBy(
                    x => x.ObjectionNo.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => x.NoticeRunLogId)
                    .First())
                .OrderBy(x => x.ObjectionNo)
                .ToList();

            var objectionNos = printedLogs
                .Select(x => x.ObjectionNo)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sourceRows = await LoadObjPropertyInfoRowsAsync(
    roll.SourceDb,
    objectionNos,
    settings.Notice,
    ct);

            if (settings.Notice.IsSection53Family())
            {
                var invalidStatusRows = new List<string>();

                foreach (var objectionNo in objectionNos)
                {
                    if (string.IsNullOrWhiteSpace(objectionNo))
                        continue;

                    var cleanObjectionNo = objectionNo.Trim();

                    var isQaPending =
                        await _sourceStatus.IsS53StatusAsync(
                            settings.RollId,
                            cleanObjectionNo,
                            NoticeWorkflowStatus.QaPending,
                            ct);

                    if (!isQaPending)
                    {
                        invalidStatusRows.Add(cleanObjectionNo);
                    }
                }

                if (invalidStatusRows.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot create QA. All S53 source records must be on " +
                        $"'{NoticeWorkflowStatus.QaPending}'. " +
                        $"Invalid records: {string.Join(", ", invalidStatusRows.Take(20))}");
                }
            }

            var joined = printedLogs
                .Join(
                    sourceRows,
                    p => p.ObjectionNo.Trim(),
                    s => s.ObjectionNo.Trim(),
                    (p, s) => new { Printed = p, Source = s },
                    StringComparer.OrdinalIgnoreCase)
                .Where(x => !string.IsNullOrWhiteSpace(x.Source.PropertyType))
                .ToList();

            if (joined.Count == 0)
                throw new InvalidOperationException("Printed notices were found, but no matching Obj_Property_Info records were found.");

            var selected = PickDynamicQaSample(
    joined,
    x => ResolveQaGroup(settings.Notice, x.Source.PropertyType, null))
    .ToList();

            var qaRun = new NoticeQaRun
            {
                WorkflowKey = workflowKey,
                NoticeSettingsId = settings.Id,
                RollId = settings.RollId,
                Notice = settings.Notice,
                Status = "Open",
                CreatedBy = user,
                CreatedAtUtc = DateTime.UtcNow
            };

            foreach (var row in selected)
            {
                var actualPropertyType = NormalizePropertyType(row.Source.PropertyType);

                var propertyType = ResolveQaGroup(
                    settings.Notice,
                    actualPropertyType,
                    null);
                var expected = GetExpectedCategory(actualPropertyType);

                var isValid = IsCategoryValid(
                    actualPropertyType,
                    row.Source.NewCategoryMvd,
                    row.Source.New2CategoryMvd,
                    row.Source.New3CategoryMvd);

                qaRun.Items.Add(new NoticeQaItem
                {
                    NoticeRunLogId = row.Printed.NoticeRunLogId,
                    ObjectionNo = row.Printed.ObjectionNo,
                    PremiseId = row.Printed.PremiseId,
                    PropertyType = propertyType,
                    PropertyDesc = row.Printed.PropertyDesc ?? row.Source.PropertyDesc,
                    PdfPath = row.Printed.PdfPath,

                    NewCategoryMvd = row.Source.NewCategoryMvd,
                    New2CategoryMvd = row.Source.New2CategoryMvd,
                    New3CategoryMvd = row.Source.New3CategoryMvd,

                    ExpectedCategory = expected,
                    IsCategoryValid = isValid,

                    QaStatus = isValid ? "Passed" : "Failed",
                    QaComment = isValid
                        ? null
                        : actualPropertyType.Equals("Multi", StringComparison.OrdinalIgnoreCase)
                            ? "Multi must have Multiple Purposes as the main category and at least one split category."
                            : "New_Category_MVD is missing. QA accepts any captured property category."
                });
            }

            _db.NoticeQaRuns.Add(qaRun);
            await _db.SaveChangesAsync(ct);

            return qaRun.Id;
        }

        public async Task ApproveQaAsync(
       Guid workflowKey,
       int qaRunId,
       string user,
       string? comment,
       CancellationToken ct)
        {
            var qaRun = await _db.NoticeQaRuns
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x =>
                    x.Id == qaRunId &&
                    x.WorkflowKey == workflowKey, ct)
                ?? throw new InvalidOperationException("QA run not found.");

            if (qaRun.Status == "Approved")
                return;

            if (qaRun.Items.Count == 0)
                throw new InvalidOperationException("Cannot approve QA because there are no QA items.");

            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == qaRun.NoticeSettingsId, ct)
                ?? throw new InvalidOperationException("Notice settings not found for QA run.");

            if (settings.Notice == NoticeKind.S49)
            {
                await ApproveS49QaAsync(
                    qaRun,
                    settings,
                    user,
                    comment,
                    ct);

                return;
            }

            if (settings.Notice == NoticeKind.TPA)
            {
                await ApproveTpaQaAsync(
                    qaRun,
                    user,
                    comment,
                    ct);

                return;
            }

            if (settings.Notice == NoticeKind.CLA_TPA)
            {
                await ApproveClaQaAsync(
                    qaRun,
                    user,
                    comment,
                    ct);

                return;
            }

            var batchIds = await _db.NoticeBatches
                .AsNoTracking()
                .Where(x => x.WorkflowKey == workflowKey && x.BatchKind == "STEP3")
                .Select(x => x.Id)
                .ToListAsync(ct);

            if (batchIds.Count == 0)
                throw new InvalidOperationException("No STEP3 batches found for this workflow.");

            var printedLogs = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x =>
                    batchIds.Contains(x.NoticeBatchId) &&
                    x.Status == RunStatus.Printed &&
                    x.ObjectionNo != null &&
                    x.ObjectionNo != "")
                .Select(x => new
                {
                    x.ObjectionNo
                })
                .ToListAsync(ct);

            if (printedLogs.Count == 0)
                throw new InvalidOperationException("No printed notices found for QA approval.");

            // ------------------------------------------------------------
            // QA item validation
            // ------------------------------------------------------------
            var failed = qaRun.Items
                .Where(x => !x.IsCategoryValid || x.QaStatus == "Failed")
                .ToList();

            if (failed.Count > 0)
                throw new InvalidOperationException("QA cannot be approved. Fix the failed category records first.");

            // ------------------------------------------------------------
            // S53 workflow gate:
            // QA can only be approved when source status is QA-Pending.
            // ------------------------------------------------------------
            if (settings.Notice.IsSection53Family())
            {
                foreach (var log in printedLogs)
                {
                    if (string.IsNullOrWhiteSpace(log.ObjectionNo))
                        continue;

                    var canApproveQa = await _sourceStatus.IsS53StatusAsync(
                        qaRun.RollId,
                        log.ObjectionNo,
                        NoticeWorkflowStatus.QaPending,
                        ct);

                    if (!canApproveQa)
                    {
                        throw new InvalidOperationException(
                            $"Cannot approve QA for S53 notice {log.ObjectionNo}. Source status must be '{NoticeWorkflowStatus.QaPending}'.");
                    }
                }
            }

            qaRun.Status = "Approved";
            qaRun.ApprovedBy = user;
            qaRun.ApprovedAtUtc = DateTime.UtcNow;
            qaRun.Comment = comment;

            await _db.SaveChangesAsync(ct);

            // ------------------------------------------------------------
            // After QA approval:
            // QA-Pending -> Email-Sent-Pending
            // ------------------------------------------------------------
            if (settings.Notice.IsSection53Family())
            {
                await _sourceStatus.SetS53StatusAsync(
                    qaRun.RollId,
                    printedLogs.Select(x => x.ObjectionNo ?? ""),
                    NoticeWorkflowStatus.EmailSentPending,
                    ct);
            }
        }

        // ============================================================
        // SECTION 49 QA
        // ============================================================

        private async Task<bool> IsS49QaApprovedAsync(
            Guid workflowKey,
            CancellationToken ct)
        {
            /*
             * Section 49 approval is batch-based.
             *
             * Every completely printed STEP3 batch must have its own
             * Approved QA run.  NoticeQaRun does not need a new batch
             * column: the batch is derived from the QA item's
             * NoticeRunLogId.
             */
            var batches = await _db.NoticeBatches
                .AsNoTracking()
                .Where(x =>
                    x.WorkflowKey == workflowKey &&
                    x.Notice == NoticeKind.S49 &&
                    x.BatchKind == "STEP3" &&
                    x.NumberOfRecords > 0)
                .Select(x => new
                {
                    x.Id,
                    x.NumberOfRecords
                })
                .ToListAsync(ct);

            if (batches.Count == 0)
                return false;

            var batchIds = batches
                .Select(x => x.Id)
                .ToList();

            var logs = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x => batchIds.Contains(x.NoticeBatchId))
                .Select(x => new
                {
                    x.Id,
                    x.NoticeBatchId,
                    x.Status,
                    x.PdfPath
                })
                .ToListAsync(ct);

            var fullyPrintedBatchIds = batches
                .Where(batch =>
                {
                    var batchLogs = logs
                        .Where(x => x.NoticeBatchId == batch.Id)
                        .ToList();

                    return batchLogs.Count == batch.NumberOfRecords &&
                           batchLogs.Count > 0 &&
                           batchLogs.All(x =>
                               x.Status == RunStatus.Printed &&
                               !string.IsNullOrWhiteSpace(x.PdfPath));
                })
                .Select(x => x.Id)
                .ToList();

            if (fullyPrintedBatchIds.Count == 0)
                return false;

            var approvedRuns = await _db.NoticeQaRuns
                .AsNoTracking()
                .Include(x => x.Items)
                .Where(x =>
                    x.WorkflowKey == workflowKey &&
                    x.Notice == NoticeKind.S49 &&
                    x.Status == "Approved")
                .ToListAsync(ct);

            foreach (var batchId in fullyPrintedBatchIds)
            {
                var batchRunLogIds = logs
                    .Where(x => x.NoticeBatchId == batchId)
                    .Select(x => x.Id)
                    .ToHashSet();

                var approvedForBatch = approvedRuns.Any(run =>
                    run.Items.Any(item =>
                        item.NoticeRunLogId.HasValue &&
                        batchRunLogIds.Contains(item.NoticeRunLogId.Value)));

                if (!approvedForBatch)
                    return false;
            }

            return true;
        }

        private async Task<int> CreateS49QaRunAsync(
     Guid workflowKey,
     NoticeSettings settings,
     string user,
     CancellationToken ct)
        {
            if (!_section49.Qa.Enabled)
            {
                throw new InvalidOperationException(
                    "Section 49 QA is disabled in configuration.");
            }

            var batches = await _db.NoticeBatches
                .AsNoTracking()
                .Where(x =>
                    x.WorkflowKey == workflowKey &&
                    x.Notice == NoticeKind.S49 &&
                    x.BatchKind == "STEP3" &&
                    x.NumberOfRecords > 0)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);

            if (batches.Count == 0)
            {
                throw new InvalidOperationException(
                    "No Section 49 batches were found for this workflow.");
            }

            /*
             * Pick the first completely printed batch that does not
             * already have an Approved QA run.
             */
            NoticeBatch? selectedBatch = null;
            List<NoticeRunLog>? selectedBatchLogs = null;

            var approvedRuns = await _db.NoticeQaRuns
                .AsNoTracking()
                .Include(x => x.Items)
                .Where(x =>
                    x.WorkflowKey == workflowKey &&
                    x.Notice == NoticeKind.S49 &&
                    x.Status == "Approved")
                .ToListAsync(ct);

            foreach (var batch in batches)
            {
                var batchLogs = await _db.NoticeRunLogs
                    .AsNoTracking()
                    .Where(x => x.NoticeBatchId == batch.Id)
                    .OrderBy(x => x.Id)
                    .ToListAsync(ct);

                if (batchLogs.Count != batch.NumberOfRecords ||
                    batchLogs.Count == 0)
                {
                    continue;
                }

                var allPrinted = batchLogs.All(x =>
                    x.Status == RunStatus.Printed &&
                    !string.IsNullOrWhiteSpace(x.PdfPath));

                if (!allPrinted)
                    continue;

                var runLogIds = batchLogs
                    .Select(x => x.Id)
                    .ToHashSet();

                var alreadyApproved = approvedRuns.Any(run =>
                    run.Items.Any(item =>
                        item.NoticeRunLogId.HasValue &&
                        runLogIds.Contains(item.NoticeRunLogId.Value)));

                if (alreadyApproved)
                    continue;

                selectedBatch = batch;
                selectedBatchLogs = batchLogs;
                break;
            }

            if (selectedBatch == null ||
                selectedBatchLogs == null)
            {
                throw new InvalidOperationException(
                    "No completely printed Section 49 batch is waiting for QA. " +
                    "Print the full batch first, or all printed batches may already be QA-approved.");
            }

            if (_section49.Qa.RequireAllNoticesPrintedBeforeQa)
            {
                if (selectedBatchLogs.Count != selectedBatch.NumberOfRecords ||
                    selectedBatchLogs.Any(x =>
                        x.Status != RunStatus.Printed ||
                        string.IsNullOrWhiteSpace(x.PdfPath)))
                {
                    throw new InvalidOperationException(
                        $"Section 49 batch '{selectedBatch.BatchName}' must be fully printed before QA can start.");
                }
            }

            var candidates = await LoadS49QaCandidatesAsync(
                settings.RollId,
                selectedBatch,
                selectedBatchLogs,
                ct);

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No Section 49 QA candidates could be resolved for batch '{selectedBatch.BatchName}'.");
            }

            /*
             * Preferred QA categories:
             * - Sectional Title
             * - Multipurpose
             * - Single Property
             *
             * IMPORTANT:
             * These are preferences only.
             *
             * If the batch does not contain one of these categories,
             * QA must still continue by selecting another printed notice
             * from the same locked batch.
             */
            var sampleRules =
                GetS49SampleRules();

            var picked =
                new List<S49QaCandidate>();

            var pickedRunLogIds =
                new HashSet<int>();

            foreach (var rule in sampleRules)
            {
                var available = candidates
                    .Where(x =>
                        string.Equals(
                            x.SampleKey,
                            rule.Key,
                            StringComparison.OrdinalIgnoreCase))
                    .Where(x =>
                        !pickedRunLogIds.Contains(
                            x.NoticeRunLogId))
                    .OrderBy(_ =>
                        Guid.NewGuid())
                    .Take(rule.Count)
                    .ToList();

                foreach (var item in available)
                {
                    item.SampleLabel =
                        rule.Label;

                    picked.Add(
                        item);

                    pickedRunLogIds.Add(
                        item.NoticeRunLogId);
                }
            }

            /*
             * The target number of QA samples is still based on the
             * configured sample rules.
             *
             * Example:
             * Sectional Title = 1
             * Multipurpose    = 1
             * Single Property = 1
             *
             * Target = 3
             *
             * If a preferred category is missing, fill the remaining
             * slots with ANY other printed notice from the SAME batch.
             */
            var targetSampleCount =
                sampleRules.Sum(x => x.Count);

            if (targetSampleCount <= 0)
            {
                targetSampleCount = 1;
            }

            var fallbackCandidates = candidates
                .Where(x =>
                    !pickedRunLogIds.Contains(
                        x.NoticeRunLogId))
                .OrderBy(_ =>
                    Guid.NewGuid())
                .ToList();

            foreach (var item in fallbackCandidates)
            {
                if (picked.Count >= targetSampleCount)
                    break;

                if (string.IsNullOrWhiteSpace(
                        item.SampleLabel))
                {
                    item.SampleLabel =
                        string.IsNullOrWhiteSpace(item.SampleKey)
                            ? "General QA"
                            : item.SampleKey;
                }

                picked.Add(
                    item);

                pickedRunLogIds.Add(
                    item.NoticeRunLogId);
            }

            if (picked.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Section 49 QA cannot be created for batch " +
                    $"'{selectedBatch.BatchName}' because no printed " +
                    $"notices were available for QA.");
            }

            /*
             * Replace only open S49 runs that belong to THIS batch.
             * An approved run for another batch remains untouched.
             */
            var selectedRunLogIds = selectedBatchLogs
                .Select(x => x.Id)
                .ToHashSet();

            var openRuns = await _db.NoticeQaRuns
                .Include(x => x.Items)
                .Where(x =>
                    x.WorkflowKey == workflowKey &&
                    x.Notice == NoticeKind.S49 &&
                    x.Status == "Open")
                .ToListAsync(ct);

            foreach (var openRun in openRuns)
            {
                var belongsToSelectedBatch = openRun.Items.Any(item =>
                    item.NoticeRunLogId.HasValue &&
                    selectedRunLogIds.Contains(
                        item.NoticeRunLogId.Value));

                if (belongsToSelectedBatch)
                {
                    openRun.Status =
                        "Replaced";
                }
            }

            var qaRun = new NoticeQaRun
            {
                WorkflowKey =
                    workflowKey,

                NoticeSettingsId =
                    settings.Id,

                RollId =
                    settings.RollId,

                Notice =
                    NoticeKind.S49,

                Status =
                    "Open",

                CreatedBy =
                    user,

                CreatedAtUtc =
                    DateTime.UtcNow
            };

            foreach (var row in picked)
            {
                var hasPremise =
                    !string.IsNullOrWhiteSpace(
                        row.PremiseId);

                var hasProperty =
                    !string.IsNullOrWhiteSpace(
                        row.PropertyDesc);

                var hasPdfPath =
                    !string.IsNullOrWhiteSpace(
                        row.PdfPath);

                var pdfExists =
                    hasPdfPath &&
                    File.Exists(
                        row.PdfPath!);

                var passed =
                    hasPremise &&
                    hasProperty &&
                    hasPdfPath &&
                    pdfExists;

                var comments =
                    new List<string>();

                if (!hasPremise)
                {
                    comments.Add(
                        "Premise ID is missing.");
                }

                if (!hasProperty)
                {
                    comments.Add(
                        "Property description is missing.");
                }

                if (!hasPdfPath)
                {
                    comments.Add(
                        "PDF path is missing.");
                }
                else if (!pdfExists)
                {
                    comments.Add(
                        "The printed PDF file does not exist on disk.");
                }

                qaRun.Items.Add(
                    new NoticeQaItem
                    {
                        NoticeRunLogId =
                            row.NoticeRunLogId,

                        /*
                         * S49 does not use an objection number.
                         * PremiseId is the audit key.
                         */
                        ObjectionNo =
                            null,

                        PremiseId =
                            row.PremiseId,

                        /*
                         * This is the QA sample label.
                         *
                         * Could be:
                         * Sectional Title
                         * Multipurpose
                         * Single Property
                         * Residential
                         * Business and Commercial
                         * General QA
                         */
                        PropertyType =
                            row.SampleLabel,

                        PropertyDesc =
                            row.PropertyDesc,

                        PdfPath =
                            row.PdfPath,

                        /*
                         * Reuse the existing QA display fields to show
                         * the actual S49 categories from the source.
                         */
                        NewCategoryMvd =
                            row.Categories.ElementAtOrDefault(0),

                        New2CategoryMvd =
                            row.Categories.ElementAtOrDefault(1),

                        New3CategoryMvd =
                            row.Categories.ElementAtOrDefault(2),

                        ExpectedCategory =
                            $"Section 49 QA sample: {row.SampleLabel}. " +
                            $"Batch: {selectedBatch.BatchName}.",

                        IsCategoryValid =
                            passed,

                        QaStatus =
                            passed
                                ? "Passed"
                                : "Failed",

                        QaComment =
                            passed
                                ? null
                                : string.Join(
                                    " ",
                                    comments)
                    });
            }

            _db.NoticeQaRuns.Add(
                qaRun);

            await _db.SaveChangesAsync(
                ct);

            await UpdateS49AuditBatchStatusAsync(
                settings.RollId,
                selectedBatch.BatchName,
                Section49Statuses.QaPending,
                ct);

            return qaRun.Id;
        }

        private async Task ApproveS49QaAsync(
       NoticeQaRun qaRun,
       NoticeSettings settings,
       string user,
       string? comment,
       CancellationToken ct)
        {
            if (qaRun.Status == "Approved")
                return;

            if (qaRun.Items.Count == 0)
            {
                throw new InvalidOperationException(
                    "Cannot approve Section 49 QA because there are no QA sample items.");
            }

            /*
             * S49 QA now validates the actual selected notices.
             *
             * Sectional Title / Multipurpose / Single Property are
             * preferred sampling categories only.
             *
             * Missing a preferred category must NOT block approval.
             */
            var failedItems = qaRun.Items
                .Where(x =>
                    !x.IsCategoryValid ||
                    string.Equals(
                        x.QaStatus,
                        "Failed",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (failedItems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Section 49 QA cannot be approved because one or more " +
                    "sample files failed validation.");
            }

            var runLogIds = qaRun.Items
                .Where(x =>
                    x.NoticeRunLogId.HasValue)
                .Select(x =>
                    x.NoticeRunLogId!.Value)
                .Distinct()
                .ToList();

            if (runLogIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Section 49 QA items are not linked to NoticeRunLogs.");
            }

            /*
             * Every QA item must belong to exactly one batch.
             */
            var batchIds = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x =>
                    runLogIds.Contains(x.Id))
                .Select(x =>
                    x.NoticeBatchId)
                .Distinct()
                .ToListAsync(ct);

            if (batchIds.Count != 1)
            {
                throw new InvalidOperationException(
                    "Section 49 QA samples must all come from the same locked batch.");
            }

            var batchId =
                batchIds[0];

            var batch = await _db.NoticeBatches
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == batchId &&
                        x.WorkflowKey == qaRun.WorkflowKey &&
                        x.Notice == NoticeKind.S49 &&
                        x.BatchKind == "STEP3",
                    ct)
                ?? throw new InvalidOperationException(
                    "The Section 49 QA batch could not be resolved.");

            /*
             * Re-confirm that the original batch is still completely printed.
             *
             * QA approval must never approve a partially printed/replaced batch.
             */
            var batchLogs = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x =>
                    x.NoticeBatchId == batch.Id)
                .ToListAsync(ct);

            if (batchLogs.Count != batch.NumberOfRecords ||
                batchLogs.Count == 0 ||
                batchLogs.Any(x =>
                    x.Status != RunStatus.Printed ||
                    string.IsNullOrWhiteSpace(x.PdfPath)))
            {
                throw new InvalidOperationException(
                    $"Section 49 batch '{batch.BatchName}' is no longer fully printed. " +
                    "QA cannot be approved.");
            }

            /*
             * Confirm every selected QA PDF still exists.
             */
            foreach (var item in qaRun.Items)
            {
                if (string.IsNullOrWhiteSpace(
                        item.PdfPath))
                {
                    throw new InvalidOperationException(
                        $"QA PDF path is missing for PremiseId '{item.PremiseId}'.");
                }

                if (!File.Exists(
                        item.PdfPath))
                {
                    throw new InvalidOperationException(
                        $"QA PDF does not exist for PremiseId '{item.PremiseId}'.");
                }
            }

            /*
             * IMPORTANT:
             *
             * We deliberately DO NOT validate:
             *
             * Sectional Title = 1
             * Multipurpose    = 1
             * Single Property = 1
             *
             * Those are sampling preferences only.
             *
             * If the batch contains no Sectional Title or Multipurpose,
             * the fallback samples selected during QA creation are valid.
             */

            qaRun.Status =
                "Approved";

            qaRun.ApprovedBy =
                user;

            qaRun.ApprovedAtUtc =
                DateTime.UtcNow;

            qaRun.Comment =
                comment;

            await _db.SaveChangesAsync(
                ct);

            /*
             * Unlock this exact batch for Section 49 sending.
             */
            await UpdateS49AuditBatchStatusAsync(
                settings.RollId,
                batch.BatchName,
                Section49Statuses.QaApproved,
                ct);
        }
        private async Task<List<S49QaCandidate>> LoadS49QaCandidatesAsync(
            int rollId,
            NoticeBatch batch,
            List<NoticeRunLog> printedLogs,
            CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.RollId == rollId,
                    ct)
                ?? throw new InvalidOperationException(
                    $"Roll {rollId} was not found.");

            if (string.IsNullOrWhiteSpace(roll.SourceDb))
            {
                throw new InvalidOperationException(
                    $"SourceDb is missing for RollId {rollId}.");
            }

            var sourceDb = roll.SourceDb.Trim();
            var source = _rollDb.GetSource(sourceDb);

            if (string.IsNullOrWhiteSpace(source.RollTable))
            {
                throw new InvalidOperationException(
                    $"RollTable is not configured for '{sourceDb}'.");
            }

            var cleanLogs = printedLogs
                .Where(x => !string.IsNullOrWhiteSpace(x.PremiseId))
                .GroupBy(
                    x => x.PremiseId!.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => x.Id)
                    .First())
                .ToList();

            if (cleanLogs.Count == 0)
                return new List<S49QaCandidate>();

            await using var cn =
                _rollConnectionFactory.Create(
                    sourceDb);

            await cn.OpenAsync(ct);

            const string createTempSql = """
                CREATE TABLE #S49Premises
                (
                    PREMISE_ID VARCHAR(50) NOT NULL PRIMARY KEY
                );
                """;

            await using (var createCmd =
                new SqlCommand(
                    createTempSql,
                    cn))
            {
                await createCmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var log in cleanLogs)
            {
                const string insertSql = """
                    INSERT INTO #S49Premises
                    (
                        PREMISE_ID
                    )
                    VALUES
                    (
                        @PremiseId
                    );
                    """;

                await using var insertCmd =
                    new SqlCommand(
                        insertSql,
                        cn);

                insertCmd.Parameters.Add(
                    new SqlParameter(
                        "@PremiseId",
                        SqlDbType.VarChar,
                        50)
                    {
                        Value = log.PremiseId!.Trim()
                    });

                await insertCmd.ExecuteNonQueryAsync(ct);
            }

            var rollTable =
                QuoteSqlIdentifier(
                    source.RollTable);

            var sql = $"""
                SELECT
                    LTRIM(RTRIM(r.PREMISEID))
                        AS PremiseId,

                    r.PropertyDesc
                        AS PropertyDesc,

                    r.CatDesc
                        AS Category

                FROM dbo.{rollTable} r

                INNER JOIN #S49Premises p
                    ON LTRIM(RTRIM(r.PREMISEID))
                     = p.PREMISE_ID

                ORDER BY
                    r.Id;
                """;

            var metadata = new List<S49RollQaRow>();

            await using (var cmd =
                new SqlCommand(
                    sql,
                    cn))
            {
                cmd.CommandTimeout = 90;

                await using var reader =
                    await cmd.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    metadata.Add(
                        new S49RollQaRow
                        {
                            PremiseId =
                                ReadString(
                                    reader,
                                    "PremiseId")
                                ?? "",

                            PropertyDesc =
                                ReadString(
                                    reader,
                                    "PropertyDesc"),

                            Category =
                                ReadString(
                                    reader,
                                    "Category")
                        });
                }
            }

            var metadataByPremise = metadata
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.PremiseId))
                .GroupBy(
                    x => x.PremiseId.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var candidates =
                new List<S49QaCandidate>();

            foreach (var log in cleanLogs)
            {
                var premiseId =
                    log.PremiseId!.Trim();

                if (!metadataByPremise.TryGetValue(
                        premiseId,
                        out var rows))
                {
                    continue;
                }

                var propertyDesc =
                    rows
                        .Select(x => x.PropertyDesc)
                        .FirstOrDefault(x =>
                            !string.IsNullOrWhiteSpace(x))
                    ?? log.PropertyDesc
                    ?? premiseId;

                var categories = rows
                    .Select(x => x.Category?.Trim())
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

                candidates.Add(
                    new S49QaCandidate
                    {
                        NoticeRunLogId = log.Id,
                        PremiseId = premiseId,
                        PropertyDesc = propertyDesc,
                        PdfPath = log.PdfPath,
                        Categories = categories,
                        SampleKey = ResolveS49SampleKey(
                            propertyDesc,
                            categories)
                    });
            }

            return candidates;
        }

        private List<Section49QaSampleOptions> GetS49SampleRules()
        {
            var configured = _section49.Qa.Samples
                ?.Where(x =>
                    !string.IsNullOrWhiteSpace(x.Key) &&
                    x.Count > 0)
                .ToList();

            if (configured != null &&
                configured.Count > 0)
            {
                return configured;
            }

            return new List<Section49QaSampleOptions>
            {
                new()
                {
                    Key = "SectionalTitle",
                    Label = "Sectional Title",
                    Count = 1
                },
                new()
                {
                    Key = "Multipurpose",
                    Label = "Multipurpose",
                    Count = 1
                },
                new()
                {
                    Key = "SingleProperty",
                    Label = "Single Property",
                    Count = 1
                }
            };
        }

        private static string ResolveS49SampleKey(
            string? propertyDesc,
            IReadOnlyCollection<string> categories)
        {
            /*
             * Multipurpose takes precedence because a multipurpose record
             * can have several roll rows/categories for the same premise.
             */
            if (categories.Any(IsS49MultipurposeValue))
                return "Multipurpose";

            if (IsS49SectionalTitle(
                    propertyDesc,
                    categories))
            {
                return "SectionalTitle";
            }

            return "SingleProperty";
        }

        private static bool IsS49MultipurposeValue(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var compact = value
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "")
                .Replace("*", "");

            return compact.Contains("MULTIPURPOSE") ||
                   compact.Contains("MULTIPLEPURPOSE");
        }

        private static bool IsS49SectionalTitle(
            string? propertyDesc,
            IEnumerable<string> categories)
        {
            if (categories.Any(x =>
                    !string.IsNullOrWhiteSpace(x) &&
                    x.Contains(
                        "SECTIONAL",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(propertyDesc))
                return false;

            var desc = propertyDesc.Trim();

            return desc.StartsWith(
                       "SS ",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   desc.StartsWith(
                       "SS-",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   desc.Contains(
                       "SECTIONAL TITLE",
                       StringComparison.OrdinalIgnoreCase);
        }

        private async Task UpdateS49AuditBatchStatusAsync(
            int rollId,
            string batchName,
            string status,
            CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.RollId == rollId,
                    ct)
                ?? throw new InvalidOperationException(
                    $"Roll {rollId} was not found.");

            if (string.IsNullOrWhiteSpace(roll.SourceDb))
                return;

            var sourceDb = roll.SourceDb.Trim();
            var source = _rollDb.GetSource(sourceDb);

            /*
             * Legacy S49 rolls do not have a configured Section49Table.
             * Their existing source workflow remains unchanged.
             */
            if (source.Section49 == null ||
                !source.Section49.HasAuditTable)
            {
                return;
            }

            var auditTable =
                QuoteSqlIdentifier(
                    source.Section49.AuditTable);

            var sql = $"""
                UPDATE dbo.{auditTable}

                SET
                    Send_Status = @Status,
                    Error_Message = NULL

                WHERE
                    Batch_Name = @BatchName;
                """;

            await using var cn =
                _rollConnectionFactory.Create(
                    sourceDb);

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn)
                {
                    CommandTimeout = 60
                };

            cmd.Parameters.Add(
                new SqlParameter(
                    "@Status",
                    SqlDbType.NVarChar,
                    50)
                {
                    Value = status
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@BatchName",
                    SqlDbType.NVarChar,
                    100)
                {
                    Value = batchName
                });

            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static string QuoteSqlIdentifier(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    "SQL identifier cannot be empty.");
            }

            return $"[{value.Replace("]", "]]")}]";
        }

        private async Task<List<ObjPropertyInfoLite>> LoadObjPropertyInfoRowsAsync(
         string sourceDb,
         List<string> objectionNos,
         NoticeKind notice,
         CancellationToken ct)
        {
            var rows = new List<ObjPropertyInfoLite>();

            if (objectionNos == null || objectionNos.Count == 0)
                return rows;

            var baseConnection =
                _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "DefaultConnection is missing.");

            await using var cn =
                new SqlConnection(baseConnection);

            await cn.OpenAsync(ct);

            var sourceDbSql = QuoteDb(sourceDb);

            // ============================================================
            // CREATE TEMP TABLE
            // Avoid dependency on dbo.StringList table type.
            // ============================================================

            const string createTempSql = @"
CREATE TABLE #ObjectionNos
(
    Objection_No NVARCHAR(100) NOT NULL PRIMARY KEY
);";

            await using (var createCmd =
                new SqlCommand(createTempSql, cn))
            {
                await createCmd.ExecuteNonQueryAsync(ct);
            }

            // ============================================================
            // INSERT OBJECTION NUMBERS
            // ============================================================

            var cleanObjectionNos = objectionNos
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var objectionNo in cleanObjectionNos)
            {
                const string insertSql = @"
INSERT INTO #ObjectionNos (Objection_No)
VALUES (@ObjectionNo);";

                await using var insertCmd =
                    new SqlCommand(insertSql, cn);

                insertCmd.Parameters.Add(
                    "@ObjectionNo",
                    SqlDbType.NVarChar,
                    100)
                    .Value = objectionNo;

                await insertCmd.ExecuteNonQueryAsync(ct);
            }

            var isRevisedMvd =
                notice == NoticeKind.S53Rev;

            // ============================================================
            // READ SOURCE DATA
            // ============================================================

            var sql = isRevisedMvd
                ? $@"
SELECT
    p.Objection_No,
    p.objection_Status,
    p.Property_Type,
    p.Property_Desc,
    p.Premise_id,

    COALESCE(
        NULLIF(
            LTRIM(RTRIM(
                CAST(
                    p.New_Category_ReviseMVD
                    AS NVARCHAR(255)
                )
            )),
            ''
        ),
        CAST(
            p.New_Category_MVD
            AS NVARCHAR(255)
        )
    ) AS New_Category_MVD,

    COALESCE(
        NULLIF(
            LTRIM(RTRIM(
                CAST(
                    p.New2_Category_ReviseMVD
                    AS NVARCHAR(255)
                )
            )),
            ''
        ),
        CAST(
            p.New2_Category_MVD
            AS NVARCHAR(255)
        )
    ) AS New2_Category_MVD,

    COALESCE(
        NULLIF(
            LTRIM(RTRIM(
                CAST(
                    p.New3_Category_ReviseMVD
                    AS NVARCHAR(255)
                )
            )),
            ''
        ),
        CAST(
            p.New3_Category_MVD
            AS NVARCHAR(255)
        )
    ) AS New3_Category_MVD

FROM {sourceDbSql}.dbo.Obj_Property_Info p

INNER JOIN #ObjectionNos n
    ON LTRIM(RTRIM(p.Objection_No))
       =
       LTRIM(RTRIM(n.Objection_No));"

                : $@"
SELECT
    p.Objection_No,
    p.objection_Status,
    p.Property_Type,
    p.Property_Desc,
    p.Premise_id,
    p.New_Category_MVD,
    p.New2_Category_MVD,
    p.New3_Category_MVD

FROM {sourceDbSql}.dbo.Obj_Property_Info p

INNER JOIN #ObjectionNos n
    ON LTRIM(RTRIM(p.Objection_No))
       =
       LTRIM(RTRIM(n.Objection_No));";

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.CommandTimeout = 90;

            await using var rd =
                await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                rows.Add(new ObjPropertyInfoLite
                {
                    ObjectionNo =
                        ReadString(
                            rd,
                            "Objection_No")
                        ?? "",

                    ObjectionStatus =
                        ReadString(
                            rd,
                            "objection_Status"),

                    PropertyType =
                        ReadString(
                            rd,
                            "Property_Type"),

                    PropertyDesc =
                        ReadString(
                            rd,
                            "Property_Desc"),

                    PremiseId =
                        ReadString(
                            rd,
                            "Premise_id"),

                    NewCategoryMvd =
                        ReadString(
                            rd,
                            "New_Category_MVD"),

                    New2CategoryMvd =
                        ReadString(
                            rd,
                            "New2_Category_MVD"),

                    New3CategoryMvd =
                        ReadString(
                            rd,
                            "New3_Category_MVD")
                });
            }

            return rows;
        }
        private static string QuoteDb(string dbName)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new InvalidOperationException("Source database name is empty.");

            return "[" + dbName.Trim().Replace("]", "]]") + "]";
        }

        private static string? ReadString(SqlDataReader rd, string column)
        {
            var ordinal = rd.GetOrdinal(column);
            return rd.IsDBNull(ordinal) ? null : rd.GetValue(ordinal)?.ToString();
        }

        private static string NormalizePropertyType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var v = value.Trim();

            if (v.Equals("Residential", StringComparison.OrdinalIgnoreCase))
                return "Res";

            if (v.Equals("Business", StringComparison.OrdinalIgnoreCase))
                return "Bus";

            if (v.Equals("Agricultural", StringComparison.OrdinalIgnoreCase))
                return "Agric";

            if (v.Equals("Multipurpose", StringComparison.OrdinalIgnoreCase))
                return "Multi";

            return v;
        }

        private static string GetExpectedCategory(string propertyType)
        {
            return propertyType.Equals("Multi", StringComparison.OrdinalIgnoreCase)
                ? "Multiple Purposes with split categories"
                : "Any captured property category in New_Category_MVD";
        }

        private static bool IsCategoryValid(
            string propertyType,
            string? newCategoryMvd,
            string? new2CategoryMvd = null,
            string? new3CategoryMvd = null)
        {
            // Do not restrict QA to Residential / Business and Commercial.
            // The source system contains many valid rating categories
            // (e.g. Agricultural, Industrial, Private Open Space, Religious,
            // Vacant Land, Public Service Infrastructure, etc.).
            //
            // For every non-Multi property type, any non-empty captured
            // New_Category_MVD is valid for QA.
            if (!propertyType.Equals("Multi", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(newCategoryMvd);
            }

            // Multipurpose is the only special case because it requires
            // the main Multiple Purposes category plus at least one split.
            var mainCategoryOk = IsMultiMainCategory(newCategoryMvd);

            var hasSplitCategory =
                !string.IsNullOrWhiteSpace(new2CategoryMvd) ||
                !string.IsNullOrWhiteSpace(new3CategoryMvd);

            return mainCategoryOk && hasSplitCategory;
        }

        private static bool IsMultiMainCategory(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var v = value.Trim();

            v = v.Replace("*", "");
            v = v.Replace(" ", "");
            v = v.Replace("-", "");
            v = v.Replace("_", "");

            v = v.ToUpperInvariant();

            return v == "MULTIPURPOSE"
                || v == "MULTIPURPOSES"
                || v == "MULTIPLEPURPOSE"
                || v == "MULTIPLEPURPOSES";
        }
        public async Task<NoticeQaRuleVm> GetQaRuleAsync(Guid workflowKey, CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey || x.WorkflowKey == workflowKey, ct)
                ?? throw new InvalidOperationException("Workflow settings not found.");

            if (settings.Notice == NoticeKind.S49)
            {
                var samples = GetS49SampleRules();
                var targetTotal = samples.Sum(x => x.Count);

                return new NoticeQaRuleVm
                {
                    TargetTotal = targetTotal,
                    MaxPerGroup = samples.Count == 0
                        ? 0
                        : samples.Max(x => x.Count),
                    GroupLabel = "Section 49 QA Sample",
                    Description =
                        "Section 49 QA uses the same fully printed locked batch and selects: " +
                        string.Join(
                            ", ",
                            samples.Select(x => $"{x.Count} {x.Label}")) +
                        "."
                };
            }

            var groupLabel = DetermineQaGroupLabel(settings.Notice);

            return new NoticeQaRuleVm
            {
                TargetTotal = QaTargetTotal,
                MaxPerGroup = QaMaxPerGroup,
                GroupLabel = groupLabel,
                Description = $"QA will select {QaTargetTotal} printed files dynamically, with a maximum of {QaMaxPerGroup} per {groupLabel}."
            };
        }
        private static List<T> PickDynamicQaSample<T>(
    IEnumerable<T> source,
    Func<T, string?> groupSelector)
        {
            var grouped = source
                .GroupBy(x => NormalizeQaGroup(groupSelector(x)))
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(_ => Guid.NewGuid()).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var picked = new List<T>();

            /*
             * First pass:
             * Take up to 3 from each group until we reach 10.
             */
            foreach (var group in grouped.Keys.OrderBy(x => x))
            {
                if (picked.Count >= QaTargetTotal)
                    break;

                var items = grouped[group];

                var takeCount = Math.Min(QaMaxPerGroup, QaTargetTotal - picked.Count);
                var take = items.Take(takeCount).ToList();

                picked.AddRange(take);
                items.RemoveAll(x => take.Contains(x));
            }

            /*
             * Fallback:
             * If there were fewer groups or fewer records, fill remaining slots,
             * still respecting max 3 per group.
             */
            while (picked.Count < QaTargetTotal)
            {
                var added = false;

                foreach (var group in grouped.Keys.OrderBy(x => x))
                {
                    if (picked.Count >= QaTargetTotal)
                        break;

                    var items = grouped[group];

                    if (items.Count == 0)
                        continue;

                    var alreadyPickedForGroup = picked.Count(x =>
                        string.Equals(
                            NormalizeQaGroup(groupSelector(x)),
                            group,
                            StringComparison.OrdinalIgnoreCase));

                    if (alreadyPickedForGroup >= QaMaxPerGroup)
                        continue;

                    picked.Add(items[0]);
                    items.RemoveAt(0);
                    added = true;
                }

                if (!added)
                    break;
            }

            return picked.Take(QaTargetTotal).ToList();
        }

        private static string ResolveQaGroup(
            NoticeKind notice,
            string? propertyType,
            string? vabName)
        {
            /*
             * VAB notices must group by VAB when VAB exists.
             * If VAB does not exist yet, fallback to property type.
             */
            if ((notice == NoticeKind.S52 || notice == NoticeKind.TPA) &&
                !string.IsNullOrWhiteSpace(vabName))
            {
                return NormalizeQaGroup(vabName);
            }

            if (!string.IsNullOrWhiteSpace(propertyType))
                return NormalizePropertyType(propertyType);

            return "General";
        }

        private static string DetermineQaGroupLabel(NoticeKind notice)
        {
            return notice switch
            {
                NoticeKind.S49 => "Section 49 QA Sample",
                NoticeKind.S52 => "VAB",
                NoticeKind.TPA => "VAB",
                NoticeKind.CLA_TPA => "Property Type",
                _ => "Property Type"
            };
        }

        private static string NormalizeQaGroup(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "General";

            var cleaned = value.Trim();

            var compact = cleaned
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "");

            return compact switch
            {
                "VAB1" => "VAB1",
                "VAB2" => "VAB2",
                "VAB3" => "VAB3",
                "VAB4" => "VAB4",
                _ => cleaned
            };
        }

        private async Task<NoticeQaVm> BuildClaQaVmAsync(
            Guid workflowKey,
            NoticeSettings settings,
            CancellationToken ct)
        {
            var totalPrinted = await _db.ClaThirdPartyApplicationNotices
                .AsNoTracking()
                .CountAsync(x =>
                    x.NoticeSettingsId == settings.Id &&
                    x.IsActive &&
                    (x.Status == "Printed" ||
                     x.Status == "Email-Failed" ||
                     x.Status == "Sent") &&
                    x.PdfPath != null &&
                    x.PdfPath != "",
                    ct);

            var qaRun = await _db.NoticeQaRuns
                .AsNoTracking()
                .Include(x => x.Items)
                .Where(x => x.WorkflowKey == workflowKey)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var vm = new NoticeQaVm
            {
                WorkflowKey = workflowKey,
                SettingsId = settings.Id,
                RollId = settings.RollId,
                RollShortCode =
                    settings.ValuationPeriodCode ??
                    settings.Roll.ToString(),
                RollName =
                    settings.RollName ??
                    "CLA Third-Party Applications",
                Notice = settings.Notice,
                VersionText = $"V{settings.Version}",
                TotalPrinted = totalPrinted,
                QaStatus = qaRun?.Status ?? "NotStarted",
                QaRunId = qaRun?.Id,
                IsApproved = qaRun?.Status == "Approved"
            };

            if (qaRun == null)
                return vm;

            var items = qaRun.Items
                .OrderBy(x => x.PropertyType)
                .ThenBy(x => x.ObjectionNo)
                .Select(x => new NoticeQaItemVm
                {
                    QaItemId = x.Id,
                    NoticeRunLogId = x.NoticeRunLogId ?? 0,
                    ObjectionNo = x.ObjectionNo,
                    PremiseId = x.PremiseId,
                    PropertyType = x.PropertyType,
                    PropertyDesc = x.PropertyDesc,
                    PdfPath = x.PdfPath,
                    NewCategoryMvd = x.NewCategoryMvd,
                    New2CategoryMvd = x.New2CategoryMvd,
                    New3CategoryMvd = x.New3CategoryMvd,
                    ExpectedCategory = x.ExpectedCategory,
                    IsCategoryValid = x.IsCategoryValid,
                    QaStatus = x.QaStatus,
                    QaComment = x.QaComment
                })
                .ToList();

            vm.TotalQaItems = items.Count;
            vm.FailedItems = items.Count(x =>
                !x.IsCategoryValid ||
                x.QaStatus == "Failed");

            vm.CanApprove =
                items.Count > 0 &&
                items.All(x => x.IsCategoryValid) &&
                qaRun.Status != "Approved";

            vm.Groups = items
                .GroupBy(x =>
                    string.IsNullOrWhiteSpace(x.PropertyType)
                        ? "General"
                        : x.PropertyType)
                .Select(group => new NoticeQaGroupVm
                {
                    PropertyType = group.Key,
                    GroupLabel = "Property Type",
                    Items = group.ToList()
                })
                .ToList();

            return vm;
        }

        private async Task<int> CreateClaQaRunAsync(
            Guid workflowKey,
            NoticeSettings settings,
            string user,
            CancellationToken ct)
        {
            var oldOpenRuns = await _db.NoticeQaRuns
                .Where(x =>
                    x.WorkflowKey == workflowKey &&
                    x.Status == "Open")
                .ToListAsync(ct);

            foreach (var oldRun in oldOpenRuns)
            {
                oldRun.Status = "Replaced";
            }

            var printedRows = await _db.ClaThirdPartyApplicationNotices
                .AsNoTracking()
                .Where(x =>
                    x.NoticeSettingsId == settings.Id &&
                    x.IsActive &&
                    (x.Status == "Printed" ||
                     x.Status == "Email-Failed" ||
                     x.Status == "Sent") &&
                    x.PdfPath != null &&
                    x.PdfPath != "")
                .OrderBy(x => x.ClaNumber)
                .Select(x => new ClaQaLite
                {
                    Id = x.Id,
                    ClaNumber = x.ClaNumber ?? "",
                    ObjectionNumber = x.ObjectionNumber ?? "",
                    PremiseId = x.PremiseId,
                    PropertyDescription = x.PropertyDescription,
                    PropertyType = x.RollCategory1,
                    PdfPath = x.PdfPath,
                    OwnerEmail = x.OwnerEmail,
                    /*
                     * QA must validate the generated ZIP, not the original
                     * source appeal-pack folder/file.
                     */
                    AppealPackZipPath = x.AppealPackZipPath,
                    AppealPackExists = x.AppealPackExists
                })
                .ToListAsync(ct);

            if (printedRows.Count == 0)
            {
                throw new InvalidOperationException(
                    "No printed CLA notices were found for QA. Print the CLA notices first.");
            }

            var selected = PickDynamicQaSample(
                    printedRows,
                    x => NormalizePropertyType(x.PropertyType))
                .ToList();

            var qaRun = new NoticeQaRun
            {
                WorkflowKey = workflowKey,
                NoticeSettingsId = settings.Id,
                RollId = settings.RollId,
                Notice = settings.Notice,
                Status = "Open",
                CreatedBy = user,
                CreatedAtUtc = DateTime.UtcNow
            };

            foreach (var row in selected)
            {
                var propertyType =
                    NormalizePropertyType(row.PropertyType);

                var hasPdf =
                    !string.IsNullOrWhiteSpace(row.PdfPath) &&
                    File.Exists(row.PdfPath);

                var hasClaNumber =
                    !string.IsNullOrWhiteSpace(row.ClaNumber);

                var hasPremise =
                    !string.IsNullOrWhiteSpace(row.PremiseId);

                var hasProperty =
                    !string.IsNullOrWhiteSpace(
                        row.PropertyDescription);

                var hasAppealPack =
                    row.AppealPackExists &&
                    !string.IsNullOrWhiteSpace(
                        row.AppealPackZipPath) &&
                    File.Exists(row.AppealPackZipPath);

                var passed =
                    hasPdf &&
                    hasClaNumber &&
                    hasPremise &&
                    hasProperty &&
                    hasAppealPack;

                var commentParts = new List<string>();

                if (!hasPdf)
                    commentParts.Add(
                        "CLA notice PDF is missing or the path does not exist.");

                if (!hasClaNumber)
                    commentParts.Add(
                        "CLA number is missing.");

                if (!hasPremise)
                    commentParts.Add(
                        "Premise ID is missing.");

                if (!hasProperty)
                    commentParts.Add(
                        "Property description is missing.");

                if (!hasAppealPack)
                    commentParts.Add(
                        "Appeal-pack ZIP is missing or the path does not exist.");

                qaRun.Items.Add(new NoticeQaItem
                {
                    /*
                     * CLA does not use NoticeRunLogs.
                     * The QA PDF is opened using the stored PdfPath.
                     */
                    NoticeRunLogId = null,

                    ObjectionNo =
                        string.IsNullOrWhiteSpace(row.ClaNumber)
                            ? row.ObjectionNumber
                            : row.ClaNumber,

                    PremiseId = row.PremiseId,
                    PropertyType = propertyType,
                    PropertyDesc =
                        row.PropertyDescription,
                    PdfPath = row.PdfPath,

                    NewCategoryMvd = "",
                    New2CategoryMvd = "",
                    New3CategoryMvd = "",

                    ExpectedCategory =
                        "CLA PDF, CLA number, Premise ID, Property Description and Appeal-Pack ZIP must exist.",

                    IsCategoryValid = passed,
                    QaStatus =
                        passed ? "Passed" : "Failed",

                    QaComment =
                        passed
                            ? null
                            : string.Join(
                                " ",
                                commentParts)
                });
            }

            _db.NoticeQaRuns.Add(qaRun);
            await _db.SaveChangesAsync(ct);

            return qaRun.Id;
        }

        private async Task ApproveClaQaAsync(
            NoticeQaRun qaRun,
            string user,
            string? comment,
            CancellationToken ct)
        {
            if (qaRun.Status == "Approved")
                return;

            if (qaRun.Items.Count == 0)
            {
                throw new InvalidOperationException(
                    "Cannot approve CLA QA because there are no QA items.");
            }

            var failedItems = qaRun.Items
                .Where(x =>
                    !x.IsCategoryValid ||
                    x.QaStatus == "Failed")
                .ToList();

            if (failedItems.Count > 0)
            {
                throw new InvalidOperationException(
                    "CLA QA cannot be approved. Fix the failed CLA QA items first.");
            }

            qaRun.Status = "Approved";
            qaRun.ApprovedBy = user;
            qaRun.ApprovedAtUtc = DateTime.UtcNow;
            qaRun.Comment = comment;

            await _db.SaveChangesAsync(ct);
        }

        private async Task<NoticeQaVm> BuildTpaQaVmAsync(
    Guid workflowKey,
    NoticeSettings settings,
    CancellationToken ct)
        {
            var totalPrinted = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .CountAsync(x =>
                    x.NoticeSettingsId == settings.Id &&
                    (x.Status == "Printed" || x.Status == "Email-Failed" || x.Status == "Sent") &&
                    x.PdfPath != null &&
                    x.PdfPath != "",
                    ct);

            var qaRun = await _db.NoticeQaRuns
                .AsNoTracking()
                .Include(x => x.Items)
                .Where(x => x.WorkflowKey == workflowKey)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var vm = new NoticeQaVm
            {
                WorkflowKey = workflowKey,
                SettingsId = settings.Id,
                RollId = settings.RollId,
                RollShortCode = settings.ValuationPeriodCode ?? "GV23",
                RollName = settings.RollName ?? "General Valuation Roll 2023",
                Notice = settings.Notice,
                VersionText = $"V{settings.Version}",
                TotalPrinted = totalPrinted,
                QaStatus = qaRun?.Status ?? "NotStarted",
                QaRunId = qaRun?.Id,
                IsApproved = qaRun?.Status == "Approved"
            };

            if (qaRun == null)
                return vm;

            var items = qaRun.Items
                .OrderBy(x => x.PropertyType)
                .ThenBy(x => x.ObjectionNo)
                .Select(x => new NoticeQaItemVm
                {
                    QaItemId = x.Id,
                    NoticeRunLogId = x.NoticeRunLogId ?? 0,
                    ObjectionNo = x.ObjectionNo,
                    PremiseId = x.PremiseId,
                    PropertyType = x.PropertyType,
                    PropertyDesc = x.PropertyDesc,
                    PdfPath = x.PdfPath,
                    NewCategoryMvd = x.NewCategoryMvd,
                    New2CategoryMvd = x.New2CategoryMvd,
                    New3CategoryMvd = x.New3CategoryMvd,
                    ExpectedCategory = x.ExpectedCategory,
                    IsCategoryValid = x.IsCategoryValid,
                    QaStatus = x.QaStatus,
                    QaComment = x.QaComment
                })
                .ToList();

            vm.TotalQaItems = items.Count;
            vm.FailedItems = items.Count(x => !x.IsCategoryValid || x.QaStatus == "Failed");

            /*
             * For TPA we are checking printed PDF existence and basic required fields.
             * If all sample items passed, allow approval.
             */
            vm.CanApprove =
                items.Count > 0 &&
                items.All(x => x.IsCategoryValid) &&
                qaRun.Status != "Approved";

            vm.Groups = items
                .GroupBy(x => string.IsNullOrWhiteSpace(x.PropertyType) ? "General" : x.PropertyType)
                .Select(g => new NoticeQaGroupVm
                {
                    PropertyType = g.Key,
                    GroupLabel = "Property Type",
                    Items = g.ToList()
                })
                .ToList();

            return vm;
        }

        private async Task<int> CreateTpaQaRunAsync(
            Guid workflowKey,
            NoticeSettings settings,
            string user,
            CancellationToken ct)
        {
            var oldOpenRuns = await _db.NoticeQaRuns
                .Where(x => x.WorkflowKey == workflowKey && x.Status == "Open")
                .ToListAsync(ct);

            foreach (var old in oldOpenRuns)
                old.Status = "Replaced";

            var printedRows = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .Where(x =>
                    x.NoticeSettingsId == settings.Id &&
                    (x.Status == "Printed" || x.Status == "Email-Failed" || x.Status == "Sent") &&
                    x.PdfPath != null &&
                    x.PdfPath != "")
                .OrderBy(x => x.Appeal_No)
                .Select(x => new TpaQaLite
                {
                    Id = x.Id,
                    AppealNo = x.Appeal_No ?? "",
                    ObjectionNo = x.Objection_No ?? "",
                    PremiseId = x.Premise_ID,
                    PropertyDesc = x.Property_Description,
                    PropertyType = x.Property_Type,
                    PdfPath = x.PdfPath,
                    OwnerEmail = x.OwnerEmail,
                    AppealPackZipPath = x.AppealPackZipPath
                })
                .ToListAsync(ct);

            if (printedRows.Count == 0)
                throw new InvalidOperationException("No printed Third-Party Appeal notices found for QA. Print the notices first.");

            var selected = PickDynamicQaSample(
                printedRows,
                x => NormalizePropertyType(x.PropertyType))
                .ToList();

            var qaRun = new NoticeQaRun
            {
                WorkflowKey = workflowKey,
                NoticeSettingsId = settings.Id,
                RollId = settings.RollId,
                Notice = settings.Notice,
                Status = "Open",
                CreatedBy = user,
                CreatedAtUtc = DateTime.UtcNow
            };

            foreach (var row in selected)
            {
                var propertyType = NormalizePropertyType(row.PropertyType);

                var hasPdf = !string.IsNullOrWhiteSpace(row.PdfPath) && File.Exists(row.PdfPath);
                var hasAppealNo = !string.IsNullOrWhiteSpace(row.AppealNo);
                var hasPremise = !string.IsNullOrWhiteSpace(row.PremiseId);
                var hasProperty = !string.IsNullOrWhiteSpace(row.PropertyDesc);

                var passed = hasPdf && hasAppealNo && hasPremise && hasProperty;

                var commentParts = new List<string>();

                if (!hasPdf)
                    commentParts.Add("PDF file is missing or path does not exist.");

                if (!hasAppealNo)
                    commentParts.Add("Appeal_No is missing.");

                if (!hasPremise)
                    commentParts.Add("Premise_ID is missing.");

                if (!hasProperty)
                    commentParts.Add("Property description is missing.");

                qaRun.Items.Add(new NoticeQaItem
                {
                    /*
                     * TPA does not use NoticeRunLogs.
                     * Keep this as 0. The QA view must use OpenThirdPartyPdf for TPA if needed later.
                     */
                    NoticeRunLogId = null,

                    ObjectionNo = string.IsNullOrWhiteSpace(row.AppealNo)
                        ? row.ObjectionNo
                        : row.AppealNo,

                    PremiseId = row.PremiseId,
                    PropertyType = propertyType,
                    PropertyDesc = row.PropertyDesc,
                    PdfPath = row.PdfPath,

                    NewCategoryMvd = "",
                    New2CategoryMvd = "",
                    New3CategoryMvd = "",

                    ExpectedCategory = "Printed PDF, Appeal No, Premise ID and Property Description must exist.",
                    IsCategoryValid = passed,

                    QaStatus = passed ? "Passed" : "Failed",
                    QaComment = passed ? null : string.Join(" ", commentParts)
                });
            }

            _db.NoticeQaRuns.Add(qaRun);
            await _db.SaveChangesAsync(ct);

            return qaRun.Id;
        }

        private async Task ApproveTpaQaAsync(
            NoticeQaRun qaRun,
            string user,
            string? comment,
            CancellationToken ct)
        {
            if (qaRun.Status == "Approved")
                return;

            if (qaRun.Items.Count == 0)
                throw new InvalidOperationException("Cannot approve QA because there are no QA items.");

            var failed = qaRun.Items
                .Where(x => !x.IsCategoryValid || x.QaStatus == "Failed")
                .ToList();

            if (failed.Count > 0)
                throw new InvalidOperationException("QA cannot be approved. Fix the failed TPA QA items first.");

            qaRun.Status = "Approved";
            qaRun.ApprovedBy = user;
            qaRun.ApprovedAtUtc = DateTime.UtcNow;
            qaRun.Comment = comment;

            await _db.SaveChangesAsync(ct);
        }



        private sealed class S49QaCandidate
        {
            public int NoticeRunLogId { get; set; }
            public string PremiseId { get; set; } = "";
            public string PropertyDesc { get; set; } = "";
            public string? PdfPath { get; set; }
            public List<string> Categories { get; set; } = new();
            public string SampleKey { get; set; } = "";
            public string SampleLabel { get; set; } = "";
        }

        private sealed class S49RollQaRow
        {
            public string PremiseId { get; set; } = "";
            public string? PropertyDesc { get; set; }
            public string? Category { get; set; }
        }

        private sealed class ClaQaLite
        {
            public int Id { get; set; }
            public string ClaNumber { get; set; } = "";
            public string ObjectionNumber { get; set; } = "";
            public string? PremiseId { get; set; }
            public string? PropertyDescription { get; set; }
            public string? PropertyType { get; set; }
            public string? PdfPath { get; set; }
            public string? OwnerEmail { get; set; }
            public string? AppealPackZipPath { get; set; }
            public bool AppealPackExists { get; set; }
        }

        private sealed class TpaQaLite
        {
            public int Id { get; set; }
            public string AppealNo { get; set; } = "";
            public string ObjectionNo { get; set; } = "";
            public string? PremiseId { get; set; }
            public string? PropertyDesc { get; set; }
            public string? PropertyType { get; set; }
            public string? PdfPath { get; set; }
            public string? OwnerEmail { get; set; }
            public string? AppealPackZipPath { get; set; }
        }
        private sealed class PrintedLogLite
        {
            public int NoticeRunLogId { get; set; }
            public string ObjectionNo { get; set; } = "";
            public string? PremiseId { get; set; }
            public string? PropertyDesc { get; set; }
            public string? PdfPath { get; set; }
        }

        private sealed class ObjPropertyInfoLite
        {
            public string ObjectionNo { get; set; } = "";
            public string? PropertyType { get; set; }
            public string? PropertyDesc { get; set; }
            public string? PremiseId { get; set; }
            public string? NewCategoryMvd { get; set; }
            public string? New2CategoryMvd { get; set; }
            public string? New3CategoryMvd { get; set; }
            public string? ObjectionStatus { get; set; }
        }
    }
}
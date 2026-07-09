// Services/QA/NoticeQaService.cs

using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace GV23_Notice.Services.QA
{
    public sealed class NoticeQaService : INoticeQaService
    {
        private readonly AppDbContext _db;
        private readonly IRollDbConnectionFactory _rollConn;
        private readonly INoticeSourceStatusService _sourceStatus;

        private const int QaTargetTotal = 10;
        private const int QaMaxPerGroup = 3;

        public NoticeQaService(
            AppDbContext db,
            IRollDbConnectionFactory rollConn,INoticeSourceStatusService sourceStatus)
        {
            _db = db;
            _rollConn = rollConn;
            _sourceStatus = sourceStatus;
        }

        public async Task<bool> RequiresQaAsync(Guid workflowKey, CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey || x.WorkflowKey == workflowKey, ct);

            if (settings == null)
                return false;

            /*
             * QA is now dynamic for normal printed notices.
             * S52 and TPA can also use QA if their printed records exist,
             * but the sample logic decides grouping dynamically.
             */
            return settings.Notice switch
            {
                NoticeKind.S49 => true,
                NoticeKind.S51 => true,
                NoticeKind.S52 => true,
                NoticeKind.S53 => true,
                NoticeKind.S53Rev => true,
                NoticeKind.DJ => true,
                NoticeKind.IN => true,
                NoticeKind.S78 => true,
                NoticeKind.TPA => true,
                _ => false
            };
        }

        public async Task<bool> IsQaApprovedAsync(Guid workflowKey, CancellationToken ct)
        {
            if (!await RequiresQaAsync(workflowKey, ct))
                return true;

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
                    NoticeRunLogId = x.NoticeRunLogId,
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
                var invalidStatusRows = sourceRows
                    .Where(x => !string.Equals(
                        x.ObjectionStatus?.Trim(),
                        NoticeWorkflowStatus.QaPending,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(x => $"{x.ObjectionNo} [{x.ObjectionStatus}]")
                    .ToList();

                if (invalidStatusRows.Any())
                {
                    throw new InvalidOperationException(
                        "Cannot create QA. All S53 source records must be on 'QA-Pending'. Invalid records: " +
                        string.Join(", ", invalidStatusRows.Take(20)));
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
                            : "Captured category does not match the expected QA rule."
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
        private async Task<List<ObjPropertyInfoLite>> LoadObjPropertyInfoRowsAsync(
      string sourceDb,
      List<string> objectionNos,
      NoticeKind notice,
      CancellationToken ct)
        {
            var rows = new List<ObjPropertyInfoLite>();

            if (objectionNos.Count == 0)
                return rows;

            await using var cn = _rollConn.Create(sourceDb);
            await cn.OpenAsync(ct);

            var table = new DataTable();
            table.Columns.Add("Value", typeof(string));

            foreach (var no in objectionNos.Distinct(StringComparer.OrdinalIgnoreCase))
                table.Rows.Add(no);

            var isRevisedMvd = notice == NoticeKind.S53Rev;

            var sql = isRevisedMvd
                ? @"
SELECT
    p.Objection_No,
    p.objection_Status,
    p.Property_Type,
    p.Property_Desc,
    p.Premise_id,

    COALESCE(NULLIF(LTRIM(RTRIM(CAST(p.New_Category_ReviseMVD AS NVARCHAR(255)))), ''), 
             CAST(p.New_Category_MVD AS NVARCHAR(255))) AS New_Category_MVD,

    COALESCE(NULLIF(LTRIM(RTRIM(CAST(p.New2_Category_ReviseMVD AS NVARCHAR(255)))), ''), 
             CAST(p.New2_Category_MVD AS NVARCHAR(255))) AS New2_Category_MVD,

    COALESCE(NULLIF(LTRIM(RTRIM(CAST(p.New3_Category_ReviseMVD AS NVARCHAR(255)))), ''), 
             CAST(p.New3_Category_MVD AS NVARCHAR(255))) AS New3_Category_MVD
FROM dbo.Obj_Property_Info p
INNER JOIN @ObjectionNos n
    ON LTRIM(RTRIM(p.Objection_No)) = LTRIM(RTRIM(n.Value));"
                : @"
SELECT
    p.Objection_No,
    p.objection_Status,
    p.Property_Type,
    p.Property_Desc,
    p.Premise_id,
    p.New_Category_MVD,
    p.New2_Category_MVD,
    p.New3_Category_MVD
FROM dbo.Obj_Property_Info p
INNER JOIN @ObjectionNos n
    ON LTRIM(RTRIM(p.Objection_No)) = LTRIM(RTRIM(n.Value));";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.CommandTimeout = 90;

            var param = cmd.Parameters.AddWithValue("@ObjectionNos", table);
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.StringList";

            await using var rd = await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                rows.Add(new ObjPropertyInfoLite
                {
                    ObjectionNo = ReadString(rd, "Objection_No") ?? "",
                    ObjectionStatus = ReadString(rd, "objection_Status"),
                    PropertyType = ReadString(rd, "Property_Type"),
                    PropertyDesc = ReadString(rd, "Property_Desc"),
                    PremiseId = ReadString(rd, "Premise_id"),

                    NewCategoryMvd = ReadString(rd, "New_Category_MVD"),
                    New2CategoryMvd = ReadString(rd, "New2_Category_MVD"),
                    New3CategoryMvd = ReadString(rd, "New3_Category_MVD")
                });
            }

            return rows;
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
                : "Check New_Category_MVD";
        }

        private static bool IsCategoryValid(
            string propertyType,
            string? newCategoryMvd,
            string? new2CategoryMvd = null,
            string? new3CategoryMvd = null)
        {
            if (!propertyType.Equals("Multi", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(newCategoryMvd);
            }

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
                NoticeKind.S52 => "VAB",
                NoticeKind.TPA => "VAB",
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
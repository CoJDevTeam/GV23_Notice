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

        private const int SamplePerPropertyType = 5;

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

            // Appeal Decision / Section 52 does not use this QA gate.
            if (settings.Notice == NoticeKind.S52)
                return false;

            // This QA rule is mainly for printed MVD notices.
            // Keep this as S53 first. After testing, we can extend it to S49/S51 if needed.
            return settings.Notice == NoticeKind.S53;
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

            vm.Groups = items
                .GroupBy(x => x.PropertyType ?? "Unknown")
                .Select(g => new NoticeQaGroupVm
                {
                    PropertyType = g.Key,
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

            var sourceRows = await LoadObjPropertyInfoRowsAsync(roll.SourceDb, objectionNos, ct);

            if (settings.Notice == NoticeKind.S53)
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

            var selected = joined
                .GroupBy(x => NormalizePropertyType(x.Source.PropertyType))
                .SelectMany(g => g
                    .OrderBy(x => x.Printed.ObjectionNo)
                    .Take(SamplePerPropertyType))
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
                var propertyType = NormalizePropertyType(row.Source.PropertyType);
                var expected = GetExpectedCategory(propertyType);

                var isValid = IsCategoryValid(
                    propertyType,
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
                        : propertyType.Equals("Multi", StringComparison.OrdinalIgnoreCase)
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
            if (settings.Notice == NoticeKind.S53)
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
            if (settings.Notice == NoticeKind.S53)
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

            var sql = @"
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

            return v.Equals("Multiple Purposes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("Multipurpose", StringComparison.OrdinalIgnoreCase)
                || v.Equals("Multi Purpose", StringComparison.OrdinalIgnoreCase)
                || v.Equals("Multi-Purpose", StringComparison.OrdinalIgnoreCase);
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
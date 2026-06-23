// Services/QA/NoticeQaService.cs

using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Rolls;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace GV23_Notice.Services.QA
{
    public sealed class NoticeQaService : INoticeQaService
    {
        private readonly AppDbContext _db;
        private readonly IRollDbConnectionFactory _rollConn;

        private const int SamplePerPropertyType = 5;

        public NoticeQaService(
            AppDbContext db,
            IRollDbConnectionFactory rollConn)
        {
            _db = db;
            _rollConn = rollConn;
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
                var isValid = IsCategoryValid(propertyType, row.Source.NewCategoryMvd);

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

            var failed = qaRun.Items
                .Where(x => !x.IsCategoryValid || x.QaStatus == "Failed")
                .ToList();

            if (failed.Count > 0)
                throw new InvalidOperationException("QA cannot be approved. Fix the failed category records first.");

            qaRun.Status = "Approved";
            qaRun.ApprovedBy = user;
            qaRun.ApprovedAtUtc = DateTime.UtcNow;
            qaRun.Comment = comment;

            await _db.SaveChangesAsync(ct);
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
                ? "Multipurpose"
                : "Check New_Category_MVD";
        }

        private static bool IsCategoryValid(string propertyType, string? newCategoryMvd)
        {
            if (propertyType.Equals("Multi", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(
                    newCategoryMvd?.Trim(),
                    "Multipurpose",
                    StringComparison.OrdinalIgnoreCase);
            }

            // For Res / Bus / Agric, we only require that New_Category_MVD is captured.
            // The QA user will visually compare it against the PDF.
            return !string.IsNullOrWhiteSpace(newCategoryMvd);
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
        }
    }
}
using System.Data;
using System.Globalization;
using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.Notices.Section52;
using GV23_Notice.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GV23_Notice.Services.Step3
{
    public sealed class S52RangePrintService : IS52RangePrintService
    {
        private readonly AppDbContext _db;
        private readonly INoticePathService _paths;
        private readonly Section52PdfService _pdf;
        private readonly IConfiguration _cfg;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<S52RangePrintService> _log;

        private string ConnStr => _cfg.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not configured.");

        public S52RangePrintService(
            AppDbContext db,
            INoticePathService paths,
            Section52PdfService pdf,
            IConfiguration cfg,
            IWebHostEnvironment env,
            ILogger<S52RangePrintService> log)
        {
            _db = db;
            _paths = paths;
            _pdf = pdf;
            _cfg = cfg;
            _env = env;
            _log = log;
        }

        // ── Count ───────────────────────────────────────────────────────────
        public async Task<int> CountRangeAsync(int settingsId, bool isReview, CancellationToken ct)
        {
            var s = await _db.NoticeSettings.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == settingsId, ct)
                    ?? throw new InvalidOperationException($"NoticeSettings {settingsId} not found.");

            if (!s.BulkFromDate.HasValue || !s.BulkToDate.HasValue) return 0;

            await using var cn = new SqlConnection(ConnStr);
            await cn.OpenAsync(ct);
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "dbo.S52_Kickoff_CountByRange";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 60;
            cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = s.RollId });
            cmd.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.Date) { Value = s.BulkFromDate.Value.Date });
            cmd.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.Date) { Value = s.BulkToDate.Value.Date });
            cmd.Parameters.Add(new SqlParameter("@IsReview", SqlDbType.Bit) { Value = isReview ? 1 : 0 });

            var val = await cmd.ExecuteScalarAsync(ct);
            return val is int i ? i : (val != null ? Convert.ToInt32(val) : 0);
        }

        // ── Print Range ─────────────────────────────────────────────────────
        public async Task<S52PrintRangeResult> PrintRangeAsync(
            int settingsId, bool isReview, string printedBy, CancellationToken ct)
        {
            var s = await _db.NoticeSettings.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == settingsId, ct)
                    ?? throw new InvalidOperationException($"NoticeSettings {settingsId} not found.");

            if (!s.IsApproved)
                throw new InvalidOperationException("Settings must be approved before printing.");
            if (!s.BulkFromDate.HasValue || !s.BulkToDate.HasValue)
                throw new InvalidOperationException("BulkFromDate / BulkToDate are required for S52 range print.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                       ?? throw new InvalidOperationException("Roll not found.");

            // Fetch all rows from SP
            var rows = await FetchAllRowsAsync(s.RollId, s.BulkFromDate.Value, s.BulkToDate.Value, isReview, ct);

            var result = new S52PrintRangeResult
            {
                Total = rows.Count,
                WorkflowKey = s.ApprovalKey ?? s.WorkflowKey ?? Guid.Empty
            };

            if (rows.Count == 0) return result;

            // Create a tracking batch (BatchKind = STEP3, auto-named)
            var shortCode = (roll.ShortCode ?? "").Replace(" ", "");
            var prefix = isReview ? $"S52R_{shortCode}_" : $"AD_{shortCode}_";
            var lastSeq = await _db.NoticeBatches.AsNoTracking()
                .Where(b => b.RollId == s.RollId && b.Notice == NoticeKind.S52
                         && b.BatchName.StartsWith(prefix))
                .OrderByDescending(b => b.Id)
                .Select(b => b.BatchName)
                .FirstOrDefaultAsync(ct);

            var nextSeq = 1;
            if (lastSeq != null)
            {
                var tail = lastSeq[prefix.Length..];
                if (int.TryParse(tail, out var parsed)) nextSeq = parsed + 1;
            }
            var batchName = $"{prefix}{nextSeq:0000}";

            var nowUtc = DateTime.UtcNow;
            var batch = new NoticeBatch
            {
                WorkflowKey = s.ApprovalKey ?? s.WorkflowKey ?? Guid.Empty,
                NoticeSettingsId = s.Id,
                RollId = s.RollId,
                Notice = NoticeKind.S52,
                BatchKind = "STEP3",
                BatchName = batchName,
                BatchDate = s.BulkFromDate.Value,
                NumberOfRecords = rows.Count,
                CreatedBy = printedBy,
                CreatedAtUtc = nowUtc
            };
            _db.NoticeBatches.Add(batch);
            await _db.SaveChangesAsync(ct);

            // Header image context (built once, reused per record)
            var headerPath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG");
            var ctx = new Section52PdfContext
            {
                HeaderImagePath = headerPath,
                LetterDate = DateOnly.FromDateTime(s.LetterDate)
            };

            // ── Step 1: Pre-insert ALL run logs as Generated ──────────────────
            // This mirrors how Step3BatchService works — the polling endpoint
            // watches Generated→Printed transitions, so logs must exist first.
            var logs = rows.Select(row => new NoticeRunLog
            {
                NoticeBatchId = batch.Id,
                AppealNo = row.AppealNo,
                ObjectionNo = row.ObjectionNo,
                PremiseId = row.PremiseId,
                RecipientEmail = row.Email,
                PropertyDesc = row.PropertyDesc,
                RecipientName = row.Addr1,
                Status = RunStatus.Generated,
                CreatedAtUtc = nowUtc
            }).ToList();

            _db.NoticeRunLogs.AddRange(logs);
            await _db.SaveChangesAsync(ct);   // All logs visible to polling as Generated

            // ── Step 2: Process each record — build PDF → update log status ───
            for (int i = 0; i < rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var row = rows[i];
                var log = logs[i];
                var appealRow = MapToAppealDecisionRow(row);

                try
                {
                    var pdfBytes = _pdf.BuildNotice(appealRow, ctx);
                    var propertyDesc = row.PropertyDesc ?? row.AppealNo ?? log.Id.ToString();

                    var pdfPath = _paths.BuildS52PdfPath(roll, row.AppealNo ?? "", propertyDesc, isReview);
                    SavePdf(pdfPath, pdfBytes);

                    log.PdfPath = pdfPath;
                    log.Status = RunStatus.Printed;
                    result.Printed++;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "S52 print failed for AppealNo={AppealNo}", row.AppealNo);
                    log.Status = RunStatus.Failed;
                    log.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                    result.Failed++;
                }

                await _db.SaveChangesAsync(ct);   // Polling sees Generated→Printed for this record
            }

            return result;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private async Task<List<S52KickoffRow>> FetchAllRowsAsync(
            int rollId, DateTime fromDate, DateTime toDate, bool isReview, CancellationToken ct)
        {
            var list = new List<S52KickoffRow>();

            await using var cn = new SqlConnection(ConnStr);
            await cn.OpenAsync(ct);
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "dbo.S52_Kickoff_SelectAllByRange";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 120;
            cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            cmd.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.Date) { Value = fromDate.Date });
            cmd.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.Date) { Value = toDate.Date });
            cmd.Parameters.Add(new SqlParameter("@IsReview", SqlDbType.Bit) { Value = isReview ? 1 : 0 });

            await using var rd = await cmd.ExecuteReaderAsync(ct);

            // Build a case-insensitive name→ordinal map
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rd.FieldCount; i++) map[rd.GetName(i)] = i;

            string? Str(string col) => map.TryGetValue(col, out var o) && !rd.IsDBNull(o) ? rd.GetString(o) : null;
            decimal? Dec(string col)
            {
                if (!map.TryGetValue(col, out var o) || rd.IsDBNull(o)) return null;
                var v = rd.GetValue(o);
                if (v is decimal d) return d;
                return decimal.TryParse(v.ToString(), out var p) ? p : null;
            }

            while (await rd.ReadAsync(ct))
            {
                list.Add(new S52KickoffRow
                {
                    AppealNo = Str("Appeal_No"),
                    ObjectionNo = Str("Objection_No"),
                    PremiseId = Str("Premise_iD"),
                    AUserId = Str("A_UserID"),
                    ValuationKey = Str("valuation_Key"),
                    PropertyDesc = Str("Property_desc"),
                    Email = Str("Email"),
                    Addr1 = Str("ADDR1"),
                    Addr2 = Str("ADDR2"),
                    Addr3 = Str("ADDR3"),
                    Addr4 = Str("ADDR4"),
                    Addr5 = Str("ADDR5"),
                    Town = Str("Town"),
                    Erf = Str("ERF"),
                    Ptn = Str("PTN"),
                    Re = Str("RE"),
                    AppMarketValue = Dec("App_Market_Value"),
                    AppMarketValue2 = Dec("App_Market_Value2"),
                    AppMarketValue3 = Dec("App_Market_Value3"),
                    AppExtent = Dec("App_Extent"),
                    AppExtent2 = Dec("App_Extent2"),
                    AppExtent3 = Dec("App_Extent3"),
                    AppCategory = Str("App_Category"),
                    AppCategory2 = Str("App_Category2"),
                    AppCategory3 = Str("App_Category3"),
                });
            }

            return list;
        }

        private static AppealDecisionRow MapToAppealDecisionRow(S52KickoffRow r)
        {
            static string N(decimal? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
            return new AppealDecisionRow
            {
                A_UserID = r.AUserId,
                Appeal_No = r.AppealNo,
                Objection_No = r.ObjectionNo,
                valuation_Key = r.ValuationKey,
                Property_desc = r.PropertyDesc,
                Email = r.Email,
                ADDR1 = r.Addr1,
                ADDR2 = r.Addr2,
                ADDR3 = r.Addr3,
                ADDR4 = r.Addr4,
                ADDR5 = r.Addr5,
                Town = r.Town,
                ERF = r.Erf,
                PTN = r.Ptn,
                RE = r.Re,
                App_Market_Value = N(r.AppMarketValue),
                App_Market_Value2 = N(r.AppMarketValue2),
                App_Market_Value3 = N(r.AppMarketValue3),
                App_Extent = N(r.AppExtent),
                App_Extent2 = N(r.AppExtent2),
                App_Extent3 = N(r.AppExtent3),
                App_Category = r.AppCategory,
                App_Category2 = r.AppCategory2,
                App_Category3 = r.AppCategory3,
            };
        }

        private static void SavePdf(string path, byte[] bytes)
        {
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, bytes);
        }

        // ── Internal DTO ─────────────────────────────────────────────────────
        private sealed class S52KickoffRow
        {
            public string? AppealNo { get; set; }
            public string? ObjectionNo { get; set; }
            public string? PremiseId { get; set; }
            public string? AUserId { get; set; }
            public string? ValuationKey { get; set; }
            public string? PropertyDesc { get; set; }
            public string? Email { get; set; }
            public string? Addr1 { get; set; }
            public string? Addr2 { get; set; }
            public string? Addr3 { get; set; }
            public string? Addr4 { get; set; }
            public string? Addr5 { get; set; }
            public string? Town { get; set; }
            public string? Erf { get; set; }
            public string? Ptn { get; set; }
            public string? Re { get; set; }
            public decimal? AppMarketValue { get; set; }
            public decimal? AppMarketValue2 { get; set; }
            public decimal? AppMarketValue3 { get; set; }
            public decimal? AppExtent { get; set; }
            public decimal? AppExtent2 { get; set; }
            public decimal? AppExtent3 { get; set; }
            public string? AppCategory { get; set; }
            public string? AppCategory2 { get; set; }
            public string? AppCategory3 { get; set; }
        }
    }
}
using GV23_Notice.Data;
using GV23_Notice.Services.Notices.Section51;
using GV23_Notice.Services.Notices.Section52;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.Notices.Section49;
using GV23_Notice.Services.Rolls;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
// ── S53 additions ──
using GV23_Notice.Services.Notices.Section53;
using GV23_Notice.Services.Notices.Section53.COJ_Notice_2026.Models.ViewModels.Section53;

namespace GV23_Notice.Services.Storage
{
    public sealed class NoticeBatchPrintService : INoticeBatchPrintService
    {
        private readonly AppDbContext _db;
        private readonly INoticePathService _paths;
        private readonly IS49RollRepository _s49Repo;
        private readonly ISection49PdfBuilder _s49Builder;
        private readonly ISection51PdfBuilder _s51Builder;
        private readonly Section52PdfService _s52Builder;
        private readonly ISection53PdfService _s53Builder;   // ← S53 added
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<NoticeBatchPrintService> _log;
        private readonly IConfiguration _config;

        public NoticeBatchPrintService(
            AppDbContext db,
            INoticePathService paths,
            IS49RollRepository s49Repo,
            ISection49PdfBuilder s49Builder,
            ISection51PdfBuilder s51Builder,
            Section52PdfService s52Builder,
            ISection53PdfService s53Builder,             // ← S53 added
            IWebHostEnvironment env,
            ILogger<NoticeBatchPrintService> log,
            IConfiguration config)
        {
            _db = db;
            _paths = paths;
            _s49Repo = s49Repo;
            _s49Builder = s49Builder;
            _s51Builder = s51Builder;
            _s52Builder = s52Builder;
            _s53Builder = s53Builder;                    // ← S53 added
            _env = env;
            _log = log;
            _config = config;
        }

        // ── Print a single batch ────────────────────────────────────────────
        public async Task<PrintBatchResult> PrintBatchAsync(
            int noticeBatchId, string printedBy, CancellationToken ct)
        {
            var batch = await _db.NoticeBatches.AsNoTracking()
                            .FirstOrDefaultAsync(b => b.Id == noticeBatchId, ct)
                        ?? throw new InvalidOperationException($"Batch {noticeBatchId} not found.");

            var settings = await _db.NoticeSettings.AsNoTracking()
                               .FirstOrDefaultAsync(s => s.Id == batch.NoticeSettingsId, ct)
                           ?? throw new InvalidOperationException("NoticeSettings not found for batch.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == batch.RollId, ct)
                       ?? throw new InvalidOperationException("Roll not found.");

            var runLogs = await _db.NoticeRunLogs
                .Where(x => x.NoticeBatchId == batch.Id
                         && (x.Status == RunStatus.Generated || x.Status == RunStatus.Failed))
                .OrderBy(x => x.Id)
                .ToListAsync(ct);

            foreach (var rl in runLogs.Where(x => x.Status == RunStatus.Failed))
                rl.Status = RunStatus.Generated;

            var result = new PrintBatchResult
            {
                BatchId = batch.Id,
                Total = runLogs.Count
            };

            foreach (var log in runLogs)
            {
                try
                {
                    await PrintOneAsync(settings, roll, batch, log, ct);
                    result.Printed++;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Print failed for RunLog {Id}", log.Id);
                    log.Status = RunStatus.Failed;
                    log.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                    result.Failed++;
                    await _db.SaveChangesAsync(ct);
                }
            }

            return result;
        }

        // ── Print ALL batches for a workflow ────────────────────────────────
        public async Task<PrintAllResult> PrintAllBatchesAsync(
            Guid workflowKey, string printedBy, CancellationToken ct)
        {
            var batches = await _db.NoticeBatches.AsNoTracking()
                .Where(b => b.WorkflowKey == workflowKey && b.BatchKind == "STEP3")
                .Select(b => b.Id)
                .ToListAsync(ct);

            var result = new PrintAllResult { TotalBatches = batches.Count };

            foreach (var batchId in batches)
            {
                var r = await PrintBatchAsync(batchId, printedBy, ct);
                result.Printed += r.Printed;
                result.Failed += r.Failed;
            }

            return result;
        }

        // ── Print one run log ───────────────────────────────────────────────
        private async Task PrintOneAsync(
            NoticeSettings settings,
            Domain.Rolls.RollRegistry roll,
            NoticeBatch batch,
            NoticeRunLog log,
            CancellationToken ct)
        {
            byte[] pdfBytes;
            string propertyDesc;

            switch (settings.Notice)
            {
                case NoticeKind.S49:
                    (pdfBytes, propertyDesc) = await BuildS49PdfAsync(settings, roll, batch, log, ct);
                    break;

                case NoticeKind.S51:
                    (pdfBytes, propertyDesc) = await BuildS51PdfAsync(settings, roll, batch, log, ct);
                    break;

                case NoticeKind.S52:
                    (pdfBytes, propertyDesc) = await BuildS52PdfAsync(settings, roll, log, ct);
                    break;

                case NoticeKind.S53:                     // ← S53 added
                    (pdfBytes, propertyDesc) = await BuildS53PdfAsync(settings, roll, batch, log, ct);
                    break;

                // DJ, IN, S78 — fall back to Step 2 approved snapshot
                default:
                    (pdfBytes, propertyDesc) = await BuildFromSnapshotAsync(settings, log, ct);
                    break;
            }

            // ── Build save path ─────────────────────────────────────────────
            string pdfPath;

            if (settings.Notice == NoticeKind.S51)
            {
                pdfPath = _paths.BuildPdfPath(
                    roll: roll,
                    notice: settings.Notice,
                    keyNo: log.ObjectionNo ?? propertyDesc,
                    propertyDesc: propertyDesc,
                    copyRole: null);
            }
            else if (settings.Notice == NoticeKind.S52)
            {
                var isReview = settings.IsSection52Review == true;
                var isPropOwner = string.Equals(log.RecipientName, "Prop Owner",
                                      StringComparison.OrdinalIgnoreCase);
                pdfPath = _paths.BuildS52PdfPath(
                    roll: roll,
                    appealNo: log.AppealNo ?? "",
                    propertyDesc: propertyDesc,
                    isReview: isReview,
                    isPropOwner: isPropOwner);
            }
            else if (settings.Notice == NoticeKind.S53)  // ← S53 path added
            {
                // RecipientName stores ObjectorType for S53 (Owner/Representative/Third_Party/Owner_Rep/Owner_Third_Party)
                var objectorType = string.IsNullOrWhiteSpace(log.RecipientName)
                    ? "Owner"
                    : log.RecipientName.Trim();

                pdfPath = _paths.BuildS53PdfPath(
                    roll: roll,
                    objectionNo: log.ObjectionNo ?? propertyDesc,
                    propertyDesc: propertyDesc,
                    objectorType: objectorType);
            }
            else
            {
                pdfPath = _paths.BuildBatchPdfPath(
                    roll: roll,
                    notice: settings.Notice,
                    batchName: batch.BatchName,
                    propertyDesc: propertyDesc,
                    copyRole: null);
            }

            SavePdf(pdfPath, pdfBytes);

            log.PdfPath = pdfPath;
            log.Status = RunStatus.Printed;
            await _db.SaveChangesAsync(ct);
        }

        // ── S49 ─────────────────────────────────────────────────────────────
        private async Task<(byte[] pdfBytes, string propertyDesc)> BuildS49PdfAsync(
            NoticeSettings settings,
            Domain.Rolls.RollRegistry roll,
            NoticeBatch batch,
            NoticeRunLog log,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(log.PremiseId))
                throw new InvalidOperationException($"RunLog {log.Id} has no PremiseId.");

            var (rows, contact) = await _s49Repo.LoadPremiseAsync(roll.RollId, log.PremiseId, ct);

            if (rows.Count == 0)
                throw new InvalidOperationException(
                    $"No roll rows found for PremiseId={log.PremiseId} in RollId={roll.RollId}.");

            static string Money(decimal v) =>
                "R " + v.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

            static string Num(decimal v) =>
                v.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

            var firstRow = rows[0];
            var propertyDesc = firstRow.PropertyDesc ?? log.PremiseId;
            var forceFour = rows.Count > 1;

            var propRows = rows.Select(r => new Section49PropertyRow
            {
                Category = r.CatDesc ?? "",
                MarketValue = Money(r.MarketValue),
                Extent = Num(r.Extent),
                Remarks = r.Reason ?? ""
            }).ToList();

            if (forceFour)
            {
                while (propRows.Count < 4) propRows.Add(new Section49PropertyRow());
                propRows = propRows.Take(4).ToList();
            }

            var pdfData = new Section49PdfData
            {
                Addr1 = contact?.Addr1 ?? "",
                Addr2 = contact?.Addr2 ?? "",
                Addr3 = contact?.Addr3 ?? "",
                Addr4 = contact?.Addr4 ?? "",
                Addr5 = contact?.Addr5 ?? "",
                PropertyDesc = propertyDesc,
                PhysicalAddress = firstRow.LisStreetAddress ?? contact?.PremiseAddress ?? "",
                ValuationKey = firstRow.ValuationKey ?? "",
                ForceFourRows = forceFour,
                PropertyRows = propRows,
                PremiseId = log.PremiseId
            };

            var ctx = new Section49NoticeContext
            {
                HeaderImagePath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG"),
                LetterDate = settings.LetterDate,
                InspectionStartDate = settings.ObjectionStartDate ?? settings.LetterDate,
                InspectionEndDate = settings.ObjectionEndDate ?? settings.LetterDate.AddDays(30),
                ExtendedEndDate = settings.ExtensionDate,
                FinancialYearsText = settings.FinancialYearsText,
                SignaturePath = settings.SignaturePath,
                ForceFourRows = forceFour,
                PropertyRows = propRows
            };

            var pdfBytes = _s49Builder.BuildNotice(pdfData, ctx);
            return (pdfBytes, propertyDesc);
        }

        // ── S51 ─────────────────────────────────────────────────────────────
        private async Task<(byte[] pdfBytes, string propertyDesc)> BuildS51PdfAsync(
            NoticeSettings settings,
            Domain.Rolls.RollRegistry roll,
            NoticeBatch batch,
            NoticeRunLog log,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(log.ObjectionNo))
                throw new InvalidOperationException($"RunLog {log.Id} has no ObjectionNo for S51 print.");

            var cs = _config.GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException("DefaultConnection is not configured.");

            Section51NoticeData data;
            DateTime closingDate;
            string propertyDesc;

            await using var conn = new SqlConnection(cs);
            await using var cmd = new SqlCommand("dbo.S51_GetNoticeRow", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            cmd.Parameters.AddWithValue("@RollId", batch.RollId);
            cmd.Parameters.AddWithValue("@ObjectionNo", log.ObjectionNo);

            await conn.OpenAsync(ct);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException(
                    $"S51_GetNoticeRow returned no row for RollId={batch.RollId}, ObjectionNo={log.ObjectionNo}.");

            static string? Str(SqlDataReader r, string col)
            {
                var v = r[col];
                return v == DBNull.Value ? null : v?.ToString();
            }

            propertyDesc = Str(reader, "PropertyDescription") ?? log.ObjectionNo ?? "";

            closingDate = reader["ClosingDate"] is DateTime cd
                ? cd
                : (settings.EvidenceCloseDate ?? settings.LetterDate.AddDays(30));

            data = new Section51NoticeData
            {
                ObjectionNo = Str(reader, "ObjectionNo") ?? log.ObjectionNo!,
                PropertyDesc = propertyDesc,
                ValuationKey = Str(reader, "ValuationKey"),
                Section51Pin = Str(reader, "Section51Pin"),
                Email = Str(reader, "RecipientEmail"),
                RollName = roll.Name ?? roll.ShortCode,
                Addr1 = Str(reader, "ADDR1") ?? "",
                Addr2 = Str(reader, "ADDR2") ?? "",
                Addr3 = Str(reader, "ADDR3") ?? "",
                Addr4 = Str(reader, "ADDR4") ?? "",
                Addr5 = Str(reader, "ADDR5") ?? "",
                Section6 = new Section6Row
                {
                    Old_Category = Str(reader, "OldCategory"),
                    Old2_Category = Str(reader, "OldCategory1"),
                    Old3_Category = Str(reader, "OldCategory2"),
                    New_Category = Str(reader, "NewCategory"),
                    New2_Category = Str(reader, "NewCategory1"),
                    New3_Category = Str(reader, "NewCategory2"),
                    Old_Market_Value = Str(reader, "OldMarketValue"),
                    Old2_Market_Value = Str(reader, "OldMarketValue1"),
                    Old3_Market_Value = Str(reader, "OldMarketValue2"),
                    New_Market_Value = Str(reader, "NewMarketValue"),
                    New2_Market_Value = Str(reader, "NewMarketValue1"),
                    New3_Market_Value = Str(reader, "NewMarketValue2"),
                    Old_Extent = Str(reader, "OldExtent"),
                    Old2_Extent = Str(reader, "OldExtent1"),
                    Old3_Extent = Str(reader, "OldExtent2"),
                    New_Extent = Str(reader, "NewExtent"),
                    New2_Extent = Str(reader, "NewExtent1"),
                    New3_Extent = Str(reader, "NewExtent2"),
                    WithEffectDate = Str(reader, "WEFDATE")
                }
            };

            var s6 = data.Section6!;
            data.IsMulti =
                !string.IsNullOrWhiteSpace(s6.Old2_Category) ||
                !string.IsNullOrWhiteSpace(s6.Old2_Market_Value) ||
                !string.IsNullOrWhiteSpace(s6.Old2_Extent) ||
                !string.IsNullOrWhiteSpace(s6.New2_Category) ||
                !string.IsNullOrWhiteSpace(s6.New2_Market_Value) ||
                !string.IsNullOrWhiteSpace(s6.New2_Extent) ||
                !string.IsNullOrWhiteSpace(s6.Old3_Category) ||
                !string.IsNullOrWhiteSpace(s6.New3_Category);

            var ctx = new Section51NoticeContext
            {
                HeaderImagePath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG"),
                LetterDate = settings.LetterDate,
                SubmissionsCloseDate = closingDate,
                PortalUrl = settings.PortalUrl ?? "https://objections.joburg.org.za",
                EnquiriesLine = settings.EnquiriesLine
                                   ?? "For any enquiries, please contact us on 011 407 6622 or valuationenquiries@joburg.org.za",
            };

            var pdfBytes = _s51Builder.BuildNotice(data, ctx);
            return (pdfBytes, propertyDesc);
        }

        // ── S52 ─────────────────────────────────────────────────────────────
        private async Task<(byte[] pdfBytes, string propertyDesc)> BuildS52PdfAsync(
            NoticeSettings settings,
            Domain.Rolls.RollRegistry roll,
            NoticeRunLog log,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(log.AppealNo))
                throw new InvalidOperationException($"RunLog {log.Id} has no AppealNo for S52 print.");

            var connStr = _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection missing.");

            bool isReview = settings.IsSection52Review == true;
            var proc = isReview
                ? "dbo.S52_Preview_SelectReviewTop1"
                : "dbo.S52_Preview_SelectAppealTop1";

            var appealType = string.IsNullOrWhiteSpace(log.RecipientName) ? null : log.RecipientName.Trim();

            AppealDecisionRow? row = null;

            await using var cn = new SqlConnection(connStr);
            await cn.OpenAsync(ct);
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = proc;
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 60;
            cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = roll.RollId });
            cmd.Parameters.Add(new SqlParameter("@AppealNo", SqlDbType.VarChar, 50) { Value = log.AppealNo.Trim() });
            if (appealType != null)
                cmd.Parameters.Add(new SqlParameter("@AppealType", SqlDbType.NVarChar, 50) { Value = appealType });

            await using var rd = await cmd.ExecuteReaderAsync(ct);

            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rd.FieldCount; i++) colMap[rd.GetName(i)] = i;

            string? Str(string col) => colMap.TryGetValue(col, out var o) && !rd.IsDBNull(o) ? rd.GetString(o) : null;
            decimal? Dec(string col)
            {
                if (!colMap.TryGetValue(col, out var o) || rd.IsDBNull(o)) return null;
                var v = rd.GetValue(o);
                if (v is decimal d) return d;
                return decimal.TryParse(v.ToString(), out var p) ? p : null;
            }
            string N(decimal? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";

            if (await rd.ReadAsync(ct))
            {
                row = new AppealDecisionRow
                {
                    A_UserID = Str("A_UserID"),
                    Appeal_No = Str("Appeal_No"),
                    Objection_No = Str("Objection_No"),
                    valuation_Key = Str("valuation_Key"),
                    Property_desc = Str("Property_desc"),
                    Email = Str("Email"),
                    ADDR1 = Str("ADDR1"),
                    ADDR2 = Str("ADDR2"),
                    ADDR3 = Str("ADDR3"),
                    ADDR4 = Str("ADDR4"),
                    ADDR5 = Str("ADDR5"),
                    Town = Str("Town"),
                    ERF = Str("ERF"),
                    PTN = Str("PTN"),
                    RE = Str("RE"),
                    App_Market_Value = N(Dec("App_Market_Value")),
                    App_Market_Value2 = N(Dec("App_Market_Value2")),
                    App_Market_Value3 = N(Dec("App_Market_Value3")),
                    App_Extent = N(Dec("App_Extent")),
                    App_Extent2 = N(Dec("App_Extent2")),
                    App_Extent3 = N(Dec("App_Extent3")),
                    App_Category = Str("App_Category"),
                    App_Category2 = Str("App_Category2"),
                    App_Category3 = Str("App_Category3"),
                };
            }

            if (row is null)
                throw new InvalidOperationException(
                    $"S52 print: no data found for AppealNo={log.AppealNo} in RollId={roll.RollId}.");

            var ctx = new Section52PdfContext
            {
                HeaderImagePath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG"),
                LetterDate = DateOnly.FromDateTime(settings.LetterDate)
            };

            var pdfBytes = _s52Builder.BuildNotice(row, ctx);
            var propertyDesc = row.Property_desc ?? log.AppealNo ?? log.Id.ToString();
            return (pdfBytes, propertyDesc);
        }

        // ── S53 ─────────────────────────────────────────────────────────────
        private async Task<(byte[] pdfBytes, string propertyDesc)> BuildS53PdfAsync(
            NoticeSettings settings,
            Domain.Rolls.RollRegistry roll,
            NoticeBatch batch,
            NoticeRunLog log,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(log.ObjectionNo))
                throw new InvalidOperationException($"RunLog {log.Id} has no ObjectionNo for S53 print.");

            var cs = _config.GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException("DefaultConnection not configured.");

            // RecipientName stores ObjectorType (set during batch creation)
            var objectorType = string.IsNullOrWhiteSpace(log.RecipientName)
                ? "Owner"
                : log.RecipientName.Trim();

            // Secondary rows (Owner_Rep / Owner_Third_Party) — the primary row
            // (Representative / Third_Party) already saved this PDF. Just record path.
            if (objectorType == "Owner_Rep" || objectorType == "Owner_Third_Party")
            {
                var secRow = await FetchMvdRowAsync(cs, batch.RollId, log.ObjectionNo, batch.BatchName, objectorType, ct);
                if (secRow == null)
                    throw new InvalidOperationException(
                        $"S53_GetMvdRow returned no row for ObjectionNo={log.ObjectionNo}, " +
                        $"ObjectorType={objectorType}, BatchName={batch.BatchName}.");

                // Set RollName from registry — SP does not return it
                secRow.RollName = roll.Name ?? roll.ShortCode ?? "Valuation Roll";

                var secDesc = secRow.PropertyDesc ?? log.ObjectionNo ?? "Property";
                var secBytes = _s53Builder.BuildNoticePdf(secRow, DateOnly.FromDateTime(settings.LetterDate));
                return (secBytes, secDesc);
            }

            // Primary row — fetch from Objection_MVD and build PDF
            var mvdRow = await FetchMvdRowAsync(cs, batch.RollId, log.ObjectionNo, batch.BatchName, objectorType, ct)
                         ?? throw new InvalidOperationException(
                             $"S53_GetMvdRow returned no row for ObjectionNo={log.ObjectionNo}, " +
                             $"BatchName={batch.BatchName}, ObjectorType={objectorType}.");

            // Set RollName from registry — SP doesn't return it
            mvdRow.RollName = roll.Name ?? roll.ShortCode ?? "Valuation Roll";

            var propertyDesc = mvdRow.PropertyDesc ?? log.ObjectionNo ?? "Property";
            var letterDate = DateOnly.FromDateTime(settings.LetterDate);

            var pdfBytes = _s53Builder.BuildNoticePdf(mvdRow, letterDate);

            // Also build the paired owner copy (Representative→Owner_Rep, Third_Party→Owner_Third_Party)
            var pairedType = objectorType switch
            {
                "Representative" => "Owner_Rep",
                "Third_Party" => "Owner_Third_Party",
                _ => null
            };

            if (pairedType != null)
            {
                var pairedRow = await FetchMvdRowAsync(cs, batch.RollId, log.ObjectionNo, batch.BatchName, pairedType, ct);
                if (pairedRow != null)
                {
                    var pairedDesc = pairedRow.PropertyDesc ?? propertyDesc;
                    var pairedBytes = _s53Builder.BuildNoticePdf(pairedRow, letterDate);
                    var pairedPath = _paths.BuildS53PdfPath(roll, log.ObjectionNo!, pairedDesc, pairedType);
                    SavePdf(pairedPath, pairedBytes);
                    _log.LogInformation("S53 paired PDF saved: {ObjectionNo} {Type} → {Path}",
                        log.ObjectionNo, pairedType, pairedPath);
                }
            }

            return (pdfBytes, propertyDesc);
        }

        // ── S53 helper: fetch one Objection_MVD row ──────────────────────────
        private async Task<Section53MvdRow?> FetchMvdRowAsync(
            string cs,
            int rollId,
            string objectionNo,
            string batchName,
            string objectorType,
            CancellationToken ct)
        {
            await using var conn = new SqlConnection(cs);
            await using var cmd = new SqlCommand("dbo.S53_GetMvdRow", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            cmd.Parameters.AddWithValue("@RollId", rollId);
            cmd.Parameters.AddWithValue("@ObjectionNo", objectionNo);
            cmd.Parameters.AddWithValue("@BatchName", batchName);
            cmd.Parameters.AddWithValue("@ObjectorType", objectorType);

            await conn.OpenAsync(ct);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct)) return null;

            // Column map — safe: any column the SP does not return gives null
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++) colMap[reader.GetName(i)] = i;

            string? Str(string col) =>
                colMap.TryGetValue(col, out var o) && !reader.IsDBNull(o)
                    ? reader.GetValue(o)?.ToString() : null;

            DateTime? DateVal(string col)
            {
                if (!colMap.TryGetValue(col, out var o) || reader.IsDBNull(o)) return null;
                var v = reader.GetValue(o);
                return v is DateTime dt ? dt : Convert.ToDateTime(v);
            }

            return new Section53MvdRow
            {
                ObjectionNo = Str("Objection_No") ?? objectionNo,
                ValuationKey = Str("ValuationKey"),
                PropertyDesc = Str("PropertyDesc"),
                Email = Str("Email"),
                Addr1 = Str("ADDR1"),
                Addr2 = Str("ADDR2"),
                Addr3 = Str("ADDR3"),
                Addr4 = Str("ADDR4"),
                Addr5 = Str("ADDR5"),
                AppealCloseDate = DateVal("AppealCloseDate"),
                Section52Review = Str("Section52Review"),
                RollName = null,   // SP doesn't return RollName — set from roll.Name in caller

                Gv_Category = Str("GV_Category"),
                Gv_Category2 = Str("GV_Category2"),
                Gv_Category3 = Str("GV_Category3"),
                Gv_Market_Value = Str("GV_Market_Value"),
                Gv_Market_Value2 = Str("GV_Market_Value2"),
                Gv_Market_Value3 = Str("GV_Market_Value3"),
                Gv_Extent = Str("GV_Extent"),
                Gv_Extent2 = Str("GV_EXtent2"),
                Gv_Extent3 = Str("GV_Extent3"),

                Mvd_Category = Str("MVD_Category"),
                Mvd_Category2 = Str("MVD_Category2"),
                Mvd_Category3 = Str("MVD_Category3"),
                Mvd_Market_Value = Str("MVD_Market_Value"),
                Mvd_Market_Value2 = Str("MVD_Market_Value2"),
                Mvd_Market_Value3 = Str("MVD_Market_Value3"),
                Mvd_Extent = Str("MVD_Extent"),
                Mvd_Extent2 = Str("MVD_Extent2"),
                Mvd_Extent3 = Str("MVD_Extent3"),
                WEFMVD= Str("wefDateMVD"),
            };
        }

        // ── Snapshot fallback (DJ, IN, S78) ─────────────────────────────────
        private async Task<(byte[] pdfBytes, string propertyDesc)> BuildFromSnapshotAsync(
            NoticeSettings settings,
            NoticeRunLog log,
            CancellationToken ct)
        {
            // Read from NoticeStep2Snapshots — saved during Step2Approve
            var snap = await _db.NoticeStep2Snapshots
                           .Where(x => x.NoticeSettingsId == settings.Id)
                           .OrderByDescending(x => x.Id)
                           .FirstOrDefaultAsync(ct);

            if (snap?.PdfBytes is null || snap.PdfBytes.Length == 0)
                throw new InvalidOperationException(
                    $"No snapshot PDF found for SettingsId={settings.Id} (notice {settings.Notice}). " +
                    "Complete Step 2 approval first — the snapshot is saved at that point.");

            var propertyDesc = !string.IsNullOrWhiteSpace(snap.PdfFileName)
                ? Path.GetFileNameWithoutExtension(snap.PdfFileName)
                : (log.ObjectionNo ?? log.PremiseId ?? $"{settings.Notice}_{settings.Id}");

            return (snap.PdfBytes, propertyDesc);
        }

        // ── Save bytes to disk ───────────────────────────────────────────────
        private static void SavePdf(string path, byte[] bytes)
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(dir))
                throw new InvalidOperationException($"Invalid PDF path: {path}");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, bytes);
        }
    }
}
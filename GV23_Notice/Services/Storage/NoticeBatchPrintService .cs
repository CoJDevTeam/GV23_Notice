using GV23_Notice.Data;
using GV23_Notice.Services.Notices.Section51;
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

namespace GV23_Notice.Services.Storage
{
    public sealed class NoticeBatchPrintService : INoticeBatchPrintService
    {
        private readonly AppDbContext _db;
        private readonly INoticePathService _paths;
        private readonly IS49RollRepository _s49Repo;
        private readonly ISection49PdfBuilder _s49Builder;
        private readonly ISection51PdfBuilder _s51Builder;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<NoticeBatchPrintService> _log;
        private readonly IConfiguration _config;

        public NoticeBatchPrintService(
            AppDbContext db,
            INoticePathService paths,
            IS49RollRepository s49Repo,
            ISection49PdfBuilder s49Builder,
            ISection51PdfBuilder s51Builder,
            IWebHostEnvironment env,
            ILogger<NoticeBatchPrintService> log,
            IConfiguration config)
        {
            _db = db;
            _paths = paths;
            _s49Repo = s49Repo;
            _s49Builder = s49Builder;
            _s51Builder = s51Builder;
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

            // Include Failed rows so re-printing a batch retries them.
            // Only skip rows already Printed or Sent.
            var runLogs = await _db.NoticeRunLogs
                .Where(x => x.NoticeBatchId == batch.Id
                         && (x.Status == RunStatus.Generated || x.Status == RunStatus.Failed))
                .OrderBy(x => x.Id)
                .ToListAsync(ct);

            // Reset failed rows back to Generated so the loop below treats them uniformly
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

                // For other notice types: fall back to snapshot-based print
                default:
                    (pdfBytes, propertyDesc) = await BuildFromSnapshotAsync(settings, log, ct);
                    break;
            }

            // Build save path — S51 uses ObjectionNo-based folder structure, not batch folder
            string pdfPath;
            if (settings.Notice == NoticeKind.S51)
            {
                // root\{ObjectionNo}\Section 51 Notice\{ObjectionNo}_{PropertyDesc}_S51.pdf
                pdfPath = _paths.BuildPdfPath(
                    roll: roll,
                    notice: settings.Notice,
                    keyNo: log.ObjectionNo ?? propertyDesc,
                    propertyDesc: propertyDesc,
                    copyRole: null);
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

        // ── S49: load roll data → build PDF ─────────────────────────────────
        private async Task<(byte[] pdfBytes, string propertyDesc)> BuildS49PdfAsync(
            NoticeSettings settings,
            Domain.Rolls.RollRegistry roll,
            NoticeBatch batch,
            NoticeRunLog log,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(log.PremiseId))
                throw new InvalidOperationException($"RunLog {log.Id} has no PremiseId.");

            // Load roll rows + sap contact for this premise
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

            // Build property rows — if multiple rows force 4, else single
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


        // ── S51: load from Section51Table via SP → build PDF ────────────────
        private async Task<(byte[] pdfBytes, string propertyDesc)> BuildS51PdfAsync(
            NoticeSettings settings,
            Domain.Rolls.RollRegistry roll,
            NoticeBatch batch,
            NoticeRunLog log,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(log.ObjectionNo))
                throw new InvalidOperationException(
                    $"RunLog {log.Id} has no ObjectionNo for S51 print.");

            // Use the same DefaultConnection — Notice_DB is the default catalog
            var cs = _config.GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException(
                         "DefaultConnection is not configured in appsettings.");

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
                    $"S51_GetNoticeRow returned no row for RollId={batch.RollId}, " +
                    $"ObjectionNo={log.ObjectionNo}. " +
                    "Check that the batch was created successfully.");

            // Helper: safely read nullable string from DBNull-safe column
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

                // PostalAddress is stored as one string in the table
                Addr1 = Str(reader, "PostalAddress") ?? "",
                Addr2 = "",
                Addr3 = "",
                Addr4 = "",
                Addr5 = "",

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
                }
            };

            var ctx = new Section51NoticeContext
            {
                HeaderImagePath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG"),
                LetterDate = settings.LetterDate,
                SubmissionsCloseDate = closingDate,
                PortalUrl = settings.PortalUrl
                                       ?? "https://objections.joburg.org.za",
                EnquiriesLine = settings.EnquiriesLine
                                       ?? "For any enquiries, please contact us on 011 407 6622 " +
                                          "or valuationenquiries@joburg.org.za",
            };

            var pdfBytes = _s51Builder.BuildNotice(data, ctx);
            return (pdfBytes, propertyDesc);
        }

        // ── Snapshot fallback for non-S49 notice types ──────────────────────
        private async Task<(byte[] pdfBytes, string propertyDesc)> BuildFromSnapshotAsync(
            NoticeSettings settings,
            NoticeRunLog log,
            CancellationToken ct)
        {
            var snap = await _db.NoticePreviewSnapshots
                           .FirstOrDefaultAsync(x => x.NoticeRunLogId == log.Id, ct);

            if (snap?.PdfBytes is null || snap.PdfBytes.Length == 0)
                throw new InvalidOperationException(
                    $"No snapshot PDF found for RunLogId={log.Id} (notice {settings.Notice}). " +
                    "Snapshots must be generated before printing.");

            var propertyDesc = snap.PropertyDesc
                               ?? snap.PdfFileName
                               ?? (log.ObjectionNo ?? log.PremiseId ?? "Property");

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
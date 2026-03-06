using GV23_Notice.Data;
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
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<NoticeBatchPrintService> _log;

        public NoticeBatchPrintService(
            AppDbContext db,
            INoticePathService paths,
            IS49RollRepository s49Repo,
            ISection49PdfBuilder s49Builder,
            IWebHostEnvironment env,
            ILogger<NoticeBatchPrintService> log)
        {
            _db = db;
            _paths = paths;
            _s49Repo = s49Repo;
            _s49Builder = s49Builder;
            _env = env;
            _log = log;
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
                .Where(x => x.NoticeBatchId == batch.Id && x.Status == RunStatus.Generated)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);

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

                // For other notice types: fall back to snapshot-based print
                default:
                    (pdfBytes, propertyDesc) = await BuildFromSnapshotAsync(settings, log, ct);
                    break;
            }

            // Build save path:  {root}\{Roll}_{Notice}\Batches\{BatchName}\{PropertyDesc}_{Notice}.pdf
            var pdfPath = _paths.BuildBatchPdfPath(
                roll: roll,
                notice: settings.Notice,
                batchName: batch.BatchName,
                propertyDesc: propertyDesc,
                copyRole: null);

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
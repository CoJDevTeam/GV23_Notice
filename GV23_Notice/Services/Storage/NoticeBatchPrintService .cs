namespace GV23_Notice.Services.Storage
{
    using global::GV23_Notice.Data;
    using global::GV23_Notice.Domain.Rolls;
    using global::GV23_Notice.Domain.Workflow;
    using global::GV23_Notice.Domain.Workflow.Entities;
    using Microsoft.EntityFrameworkCore;

    namespace GV23_Notice.Services.Workflow.Step3
    {
        public sealed class NoticeBatchPrintService : INoticeBatchPrintService
        {
            private readonly AppDbContext _db;
            private readonly INoticePathService _paths;

            public NoticeBatchPrintService(
                AppDbContext db,
                INoticePathService paths)
            {
                _db = db;
                _paths = paths;
            }

            public async Task<PrintBatchResult> PrintBatchAsync(int noticeBatchId, string printedBy, CancellationToken ct)
            {
                var batch = await _db.NoticeBatches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == noticeBatchId, ct)
                    ?? throw new InvalidOperationException("Batch not found.");

                var settings = await _db.NoticeSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.WorkflowKey == batch.WorkflowKey, ct)
                    ?? throw new InvalidOperationException("NoticeSettings not found for batch.");

                var roll = await _db.RollRegistry
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.RollId == batch.RollId, ct)
                    ?? throw new InvalidOperationException("Roll not found.");

                // Pull runlogs that still need pdf
                var runLogs = await _db.NoticeRunLogs
                    .Where(x => x.NoticeBatchId == batch.Id
                             && (x.PdfPath == null || x.PdfPath == "")
                             && x.Status == RunStatus.Generated)
                    .OrderBy(x => x.Id)
                    .Take(2000)
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
                        await PrintOneFromSnapshotAsync(settings, roll, batch, log, ct);

                        // If you have RunStatus.Printed, set it here.
                        // If not, keep Generated and only set Sent later in email step.
                        // Recommended: add Printed.
                        log.ErrorMessage = null;

                        result.Printed++;
                    }
                    catch (Exception ex)
                    {
                        log.Status = RunStatus.Failed;
                        log.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                        result.Failed++;
                    }

                    await _db.SaveChangesAsync(ct);
                }

                return result;
            }

            private async Task PrintOneFromSnapshotAsync(
                NoticeSettings settings,
                RollRegistry roll,
                NoticeBatch batch,
                NoticeRunLog log,
                CancellationToken ct)
            {
                // ✅ Snapshot must be linked to the runlog
                // REQUIRED: NoticePreviewSnapshot.NoticeRunLogId OR (BatchId + key fields)
                var snap = await _db.NoticePreviewSnapshots
                    .FirstOrDefaultAsync(x => x.NoticeRunLogId == log.Id, ct);

                if (snap is null)
                    throw new InvalidOperationException($"Snapshot not found for RunLogId={log.Id}. Create snapshot rows during Step3-Step2.");

                if (snap.PdfBytes is null || snap.PdfBytes.Length == 0)
                    throw new InvalidOperationException($"Snapshot PdfBytes missing for RunLogId={log.Id}. Snapshot must store PdfBytes.");

                // Decide keyNo based on notice
                var keyNo = settings.Notice switch
                {
                    NoticeKind.S49 => "",
                    NoticeKind.S52 => snap.AppealNo ?? log.AppealNo ?? "",
                    _ => snap.ObjectionNo ?? log.ObjectionNo ?? ""
                };

                // Property description for folder/filename
                // You MUST store this in snapshot, otherwise we cannot guarantee correct naming.
                // If you don't have PropertyDesc field in snapshot yet, we fallback.
                var propertyDesc =
                    !string.IsNullOrWhiteSpace(snap.PropertyDesc) ? snap.PropertyDesc :
                    !string.IsNullOrWhiteSpace(snap.PdfFileName) ? snap.PdfFileName :
                    "Property";

                // Optional: CopyRole (“OWNER”/”REP”) stored in snapshot (recommended)
                var copyRole = string.IsNullOrWhiteSpace(snap.CopyRole) ? null : snap.CopyRole;

                // Build path using your existing rules in INoticePathService
                var pdfPath = _paths.BuildPdfPath(
                    roll: roll,
                    notice: settings.Notice,
                    keyNo: keyNo,
                    propertyDesc: propertyDesc,
                    copyRole: copyRole);

                SavePdf(pdfPath, snap.PdfBytes);

                // Update runlog
                log.PdfPath = pdfPath;

                // If you add Printed status:
                // log.Status = RunStatus.Printed;

                // Also, if you want audit “who printed”, create an AuditTrail row here (recommended)
                // Example:
                // _db.AuditTrail.Add(new AuditTrail { ... Action="PRINT", By=printedBy, RunLogId=log.Id ... });
            }

            private static void SavePdf(string path, byte[] bytes)
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(dir))
                    throw new InvalidOperationException("Invalid PDF path.");

                Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, bytes);
            }
        }
    }
}
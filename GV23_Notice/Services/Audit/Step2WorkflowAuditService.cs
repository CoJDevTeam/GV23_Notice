using GV23_Notice.Data;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.Preview;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace GV23_Notice.Services.Audit
{
    public sealed class Step2WorkflowAuditService : IStep2WorkflowAuditService
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public Step2WorkflowAuditService(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public async Task<Guid> EnsureWorkflowKeyAsync(int settingsId, CancellationToken ct)
        {
            var s = await _db.NoticeSettings.FirstOrDefaultAsync(x => x.Id == settingsId, ct)
                ?? throw new InvalidOperationException("NoticeSettings not found.");

            if (s.WorkflowKey is null || s.WorkflowKey == Guid.Empty)
            {
                s.WorkflowKey = Guid.NewGuid();
                await _db.SaveChangesAsync(ct);
            }

            return s.WorkflowKey.Value;
        }

        public async Task<long> LogStep2ApprovedAsync(
            int settingsId,
            PreviewVariant variant,
            PreviewMode mode,
            string approvedBy,
            string notifyToEmail,
            string approvalSubject,
            string approvalBodyHtml,
            string kickoffUrl,
            byte[] previewPdfBytes,
            string previewPdfFileName,
            CancellationToken ct)
        {
            var (s, roll) = await LoadSettingsAndRollAsync(settingsId, ct);

            var key = await EnsureWorkflowKeyAsync(settingsId, ct);

            // Ensure step2 batch exists
            var batch = await EnsureStep2BatchAsync(s, key, approvedBy, ct);

            // Save files (optional but recommended for audit)
            var pdfPath = SaveAuditPdfIfAny(batch, key, "APPROVED", previewPdfBytes, previewPdfFileName);
            var emlPath = SaveAuditEml(batch, key, "APPROVED", notifyToEmail, approvalSubject, approvalBodyHtml);

            var log = new NoticeRunLog
            {
                NoticeBatchId = batch.Id,
                RecipientEmail = notifyToEmail,
                PdfPath = pdfPath,
                EmlPath = emlPath,
                Status = RunStatus.Generated,
                ErrorMessage = null,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.NoticeRunLogs.Add(log);

            // Mark Step2 approved on settings
            s.Step2Approved = true;
            s.Step2ApprovedAt = DateTime.UtcNow;
            s.Step2ApprovedBy = approvedBy;

            s.Step2CorrectionRequested = false;
            s.Step2CorrectionRequestedAt = null;
            s.Step2CorrectionRequestedBy = null;
            s.Step2CorrectionReason = null;

            await _db.SaveChangesAsync(ct);

            // Store details in batch note/metadata if you have fields. If not, keep it in snapshot table.
            await SaveStep2EventSnapshotAsync(
                settingsId: s.Id,
                workflowKey: key,
                action: "STEP2_APPROVED",
                actor: approvedBy,
                notifyTo: notifyToEmail,
                subject: approvalSubject,
                bodyHtml: approvalBodyHtml,
                extra: new { kickoffUrl, variant = variant.ToString(), mode = mode.ToString() },
                ct: ct);

            return log.Id;
        }

        public async Task<long> LogStep2CorrectionRequestedAsync(
            int settingsId,
            PreviewVariant variant,
            PreviewMode mode,
            string requestedBy,
            string notifyToEmail,
            string reason,
            string correctionSubject,
            string correctionBodyHtml,
            byte[] previewPdfBytes,
            string previewPdfFileName,
            CancellationToken ct)
        {
            var (s, roll) = await LoadSettingsAndRollAsync(settingsId, ct);

            var key = await EnsureWorkflowKeyAsync(settingsId, ct);

            var batch = await EnsureStep2BatchAsync(s, key, requestedBy, ct);

            var pdfPath = SaveAuditPdfIfAny(batch, key, "CORRECTION", previewPdfBytes, previewPdfFileName);
            var emlPath = SaveAuditEml(batch, key, "CORRECTION", notifyToEmail, correctionSubject, correctionBodyHtml);

            var log = new NoticeRunLog
            {
                NoticeBatchId = batch.Id,
                RecipientEmail = notifyToEmail,
                PdfPath = pdfPath,
                EmlPath = emlPath,
                Status = RunStatus.Generated,
                ErrorMessage = null,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.NoticeRunLogs.Add(log);

            // Mark correction requested on settings
            s.Step2CorrectionRequested = true;
            s.Step2CorrectionRequestedAt = DateTime.UtcNow;
            s.Step2CorrectionRequestedBy = requestedBy;
            s.Step2CorrectionReason = (reason ?? "").Trim();

            await _db.SaveChangesAsync(ct);

            await SaveStep2EventSnapshotAsync(
                settingsId: s.Id,
                workflowKey: key,
                action: "STEP2_CORRECTION_REQUESTED",
                actor: requestedBy,
                notifyTo: notifyToEmail,
                subject: correctionSubject,
                bodyHtml: correctionBodyHtml,
                extra: new { reason = s.Step2CorrectionReason, variant = variant.ToString(), mode = mode.ToString() },
                ct: ct);

            return log.Id;
        }

        // -------------------------
        // Helpers
        // -------------------------

        private async Task<(NoticeSettings settings, RollRegistry roll)> LoadSettingsAndRollAsync(int settingsId, CancellationToken ct)
        {
            var s = await _db.NoticeSettings.FirstOrDefaultAsync(x => x.Id == settingsId, ct)
                ?? throw new InvalidOperationException("NoticeSettings not found.");

            var roll = await _db.RollRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                ?? throw new InvalidOperationException("RollRegistry not found.");

            return (s, roll);
        }

        private async Task<NoticeBatch> EnsureStep2BatchAsync(NoticeSettings s, Guid key, string actor, CancellationToken ct)
        {
            // Try find existing STEP2_AUDIT batch for same settings+version
            var batch = await _db.NoticeBatches
                .FirstOrDefaultAsync(b =>
                    b.BatchKind == "STEP2_AUDIT" &&
                    b.WorkflowKey == key &&
                    b.RollId == s.RollId &&
                    b.Notice == s.Notice &&
                    b.Version == s.Version.ToString(), ct);

            if (batch != null) return batch;

            batch = new NoticeBatch
            {
                BatchKind = "STEP2_AUDIT",
                WorkflowKey = key,
                RollId = s.RollId,
                Notice = s.Notice,
                Mode = s.Mode,
                Version = s.Version.ToString(),
                CreatedBy = actor,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.NoticeBatches.Add(batch);
            await _db.SaveChangesAsync(ct);

            return batch;
        }

        private string? SaveAuditPdfIfAny(NoticeBatch batch, Guid key, string action, byte[] pdfBytes, string pdfFileName)
        {
            if (pdfBytes is null || pdfBytes.Length == 0) return null;

            var root = Path.Combine(_env.ContentRootPath, "App_Data", "Step2Audit", key.ToString("N"));
            Directory.CreateDirectory(root);

            var safeName = string.IsNullOrWhiteSpace(pdfFileName) ? "preview.pdf" : Path.GetFileName(pdfFileName);
            var file = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{action}_{safeName}";
            var full = Path.Combine(root, file);

            File.WriteAllBytes(full, pdfBytes);
            return full;
        }

        private string SaveAuditEml(NoticeBatch batch, Guid key, string action, string to, string subject, string bodyHtml)
        {
            var root = Path.Combine(_env.ContentRootPath, "App_Data", "Step2Audit", key.ToString("N"));
            Directory.CreateDirectory(root);

            // Simple .eml-like text (if you already generate real .eml files, replace this)
            var file = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{action}_workflow.eml";
            var full = Path.Combine(root, file);

            var sb = new StringBuilder();
            sb.AppendLine($"To: {to}");
            sb.AppendLine($"Subject: {subject}");
            sb.AppendLine("MIME-Version: 1.0");
            sb.AppendLine("Content-Type: text/html; charset=UTF-8");
            sb.AppendLine();
            sb.AppendLine(bodyHtml);

            File.WriteAllText(full, sb.ToString(), Encoding.UTF8);
            return full;
        }

        // If you already have snapshot models, store “email sent / approval/correction email” snapshots here
        private async Task SaveStep2EventSnapshotAsync(
            int settingsId,
            Guid workflowKey,
            string action,
            string actor,
            string notifyTo,
            string subject,
            string bodyHtml,
            object? extra,
            CancellationToken ct)
        {
            // If you don't have a table for this, you can skip.
            // But you asked: "this email must be in the audit trail"
            // We are already saving it as an EML file + RunLog row.
            // Optionally: store JSON in NoticeRunLog.ErrorMessage is wrong; do not.
            await Task.CompletedTask;
        }
    }
}

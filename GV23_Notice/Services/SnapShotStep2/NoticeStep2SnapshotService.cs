using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.Preview;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GV23_Notice.Services.SnapShotStep2
{
    public sealed class NoticeStep2SnapshotService : INoticeStep2SnapshotService
    {
        private readonly AppDbContext _db;

        public NoticeStep2SnapshotService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<int> SaveApprovalAsync(
            int settingsId,
            PreviewVariant variant,
            PreviewMode mode,
            string approvedBy,
            string emailSubject,
            string emailBodyHtml,
            byte[] pdfBytes,
            string pdfFileName,
            CancellationToken ct)
        {
            var settings = await _db.NoticeSettings.FirstOrDefaultAsync(x => x.Id == settingsId, ct);
            if (settings is null) throw new InvalidOperationException("NoticeSettings not found.");

            if (!settings.IsApproved)
                throw new InvalidOperationException("Step1 must be approved before Step2 approval.");

            // ── Idempotent: if already approved return existing snapshot ──
            // This happens when a user comes back to preview an already-approved
            // workflow and clicks "Start Step 3" again. We don't re-save the
            // snapshot — we just return the existing snapshot ID so the caller
            // can redirect to Step 3 Kickoff without hitting an exception.
            if (settings.Step2Approved)
            {
                var existing = await _db.NoticeStep2Snapshots
                    .AsNoTracking()
                    .Where(x => x.NoticeSettingsId == settingsId)
                    .OrderByDescending(x => x.Id)
                    .Select(x => x.Id)
                    .FirstOrDefaultAsync(ct);
                return existing;   // 0 if somehow no snapshot row, caller ignores return value
            }

            var roll = await _db.RollRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.RollId == settings.RollId, ct);
            if (roll is null) throw new InvalidOperationException("RollRegistry not found.");

            var snap = new NoticeStep2Snapshot
            {
                NoticeSettingsId = settings.Id,
                RollId = settings.RollId,
                Notice = settings.Notice,
                Variant = variant,
                Mode = mode,
                Version = settings.Version.ToString(),
                EmailSubjectSnapshot = emailSubject ?? "",
                EmailBodyHtmlSnapshot = emailBodyHtml ?? "",
                // Store PDF bytes so BuildFromSnapshotAsync can replay the exact PDF at print time
                PdfBytes = pdfBytes is { Length: > 0 } ? pdfBytes : null,
                PdfFileName = pdfFileName ?? "",
                PdfSha256 = Sha256Hex(pdfBytes),
                CreatedBy = approvedBy ?? "",
                CreatedAt = DateTime.UtcNow,
                SettingsJsonSnapshot = JsonSerializer.Serialize(settings)
            };

            _db.NoticeStep2Snapshots.Add(snap);

            // update settings state
            settings.Step2Approved = true;
            settings.Step2ApprovedAt = DateTime.UtcNow;
            settings.Step2ApprovedBy = approvedBy;

            // clear correction flags if any
            settings.Step2CorrectionRequested = false;
            settings.Step2CorrectionRequestedAt = null;
            settings.Step2CorrectionRequestedBy = null;
            settings.Step2CorrectionReason = null;

            await _db.SaveChangesAsync(ct);
            return snap.Id;
        }

        public async Task<int> SaveCorrectionAsync(
            int settingsId,
            PreviewVariant variant,
            PreviewMode mode,
            string requestedBy,
            string reason,
            string emailSubject,
            string emailBodyHtml,
            byte[] pdfBytes,
            string pdfFileName,
            CancellationToken ct)
        {
            var settings = await _db.NoticeSettings.FirstOrDefaultAsync(x => x.Id == settingsId, ct);
            if (settings is null) throw new InvalidOperationException("NoticeSettings not found.");

            if (!settings.IsApproved)
                throw new InvalidOperationException("Step1 must be approved before requesting correction.");

            // if already approved, don’t allow correction request
            if (settings.Step2Approved)
                throw new InvalidOperationException("Cannot request correction after Step2 approval.");

            var roll = await _db.RollRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.RollId == settings.RollId, ct);
            if (roll is null) throw new InvalidOperationException("RollRegistry not found.");

            var cr = new NoticeStep2CorrectionRequest
            {
                NoticeSettingsId = settings.Id,
                RollId = settings.RollId,
                Notice = settings.Notice,
                Variant = variant,
                Mode = mode,
                Version = settings.Version.ToString(),
                Reason = (reason ?? "").Trim(),
                EmailSubjectSnapshot = emailSubject ?? "",
                EmailBodyHtmlSnapshot = emailBodyHtml ?? "",
                PdfFileName = pdfFileName ?? "",
                PdfSha256 = Sha256Hex(pdfBytes),
                RequestedBy = requestedBy ?? "",
                RequestedAt = DateTime.UtcNow,
                SettingsJsonSnapshot = JsonSerializer.Serialize(settings)
            };

            _db.NoticeStep2CorrectionRequests.Add(cr);

            // update settings state
            settings.Step2CorrectionRequested = true;
            settings.Step2CorrectionRequestedAt = DateTime.UtcNow;
            settings.Step2CorrectionRequestedBy = requestedBy;
            settings.Step2CorrectionReason = cr.Reason;

            await _db.SaveChangesAsync(ct);
            return cr.Id;
        }

        private static string Sha256Hex(byte[] data)
        {
            if (data is null || data.Length == 0) return "";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
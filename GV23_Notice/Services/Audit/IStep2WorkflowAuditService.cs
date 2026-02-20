using GV23_Notice.Services.Preview;

namespace GV23_Notice.Services.Audit
{
    public interface IStep2WorkflowAuditService
    {
        Task<Guid> EnsureWorkflowKeyAsync(int settingsId, CancellationToken ct);

        Task<long> LogStep2ApprovedAsync(
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
            CancellationToken ct);

        Task<long> LogStep2CorrectionRequestedAsync(
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
            CancellationToken ct);
    }
}

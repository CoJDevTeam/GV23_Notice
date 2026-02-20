using GV23_Notice.Services.Preview;

namespace GV23_Notice.Services.SnapShotStep2
{
    public interface INoticeStep2SnapshotService
    {
        Task<int> SaveApprovalAsync(int settingsId, PreviewVariant variant, PreviewMode mode, string approvedBy, string emailSubject, string emailBodyHtml, byte[] pdfBytes, string pdfFileName, CancellationToken ct);
        Task<int> SaveCorrectionAsync(int settingsId, PreviewVariant variant, PreviewMode mode, string requestedBy, string reason, string emailSubject, string emailBodyHtml, byte[] pdfBytes, string pdfFileName, CancellationToken ct);
    }
}

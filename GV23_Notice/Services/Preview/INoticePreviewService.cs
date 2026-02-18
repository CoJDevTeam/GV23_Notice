using GV23_Notice.Models.DTOs;

namespace GV23_Notice.Services.Preview
{
    public interface INoticePreviewService
    {
        // Backward compatible wrapper
        Task<NoticePreviewResult> BuildPreviewAsync(
            int settingsId,
            PreviewVariant variant,
            bool isMulti,
            CancellationToken ct);

        // Primary (Single / EmailMulti / SplitPdf)
        Task<NoticePreviewResult> BuildPreviewAsync(
            int settingsId,
            PreviewVariant variant,
            PreviewMode mode,
            CancellationToken ct);
    }
}

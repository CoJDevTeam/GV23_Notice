using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.DTOs;

namespace GV23_Notice.Services.Preview
{
    public interface IPreviewDbDataService
    {
        Task<S49PreviewDbData> S49PreviewDbDataAsync(int rollId, bool split, CancellationToken ct);
        Task<S51PreviewDbData> S51PreviewDbDataAsync(int rollId, bool preferMulti, CancellationToken ct);
        Task<S52PreviewDbData> S52PreviewDbDataAsync(int rollId, string appealNo, bool isReview, CancellationToken ct);
        Task<S53PreviewDbData> S53PreviewDbDataAsync(int rollId, bool preferMulti, CancellationToken ct);
        Task<DJPreviewDbData> DJPreviewDbDataAsync(int rollId, CancellationToken ct);
        Task<InvalidPreviewDbData> InvalidPreviewDbDataAsync(int rollId, bool isOmission, CancellationToken ct);


        // Convenience (if you prefer the signatures you requested)
        Task<NoticePreviewSnapshot> S49ByPremiseIdAsync(int settingsId, string premiseId, CancellationToken ct);
        Task<NoticePreviewSnapshot> S51ByObjectionNoAsync(int settingsId, string objectionNo, CancellationToken ct);
        Task<NoticePreviewSnapshot> S52ByAppealNoAsync(int settingsId, string appealNo, bool isReview, CancellationToken ct);
        Task<NoticePreviewSnapshot> S53ByObjectionNoAsync(int settingsId, string objectionNo, CancellationToken ct);
        Task<NoticePreviewSnapshot> DJByObjectionNoAsync(int settingsId, string objectionNo, CancellationToken ct);
        Task<NoticePreviewSnapshot> InvalidByObjectionNoAsync(int settingsId, string objectionNo, bool isOmission, CancellationToken ct);

    }
}

using GV23_Notice.Models.DTOs;

namespace GV23_Notice.Services.Preview
{
    public interface IPreviewDbDataService
    {
        Task<S49PreviewDbData> S49PreviewDbDataAsync(int rollId, bool split, CancellationToken ct);
        Task<S51PreviewDbData> S51PreviewDbDataAsync(int rollId, CancellationToken ct);
        Task<S52PreviewDbData> S52PreviewDbDataAsync(int rollId, string appealNo, bool isReview, CancellationToken ct);
        Task<S53PreviewDbData> S53PreviewDbDataAsync(int rollId, CancellationToken ct);
        Task<DJPreviewDbData> DJPreviewDbDataAsync(int rollId, CancellationToken ct);
        Task<InvalidPreviewDbData> InvalidPreviewDbDataAsync(int rollId, bool isOmission, CancellationToken ct);
    }
}

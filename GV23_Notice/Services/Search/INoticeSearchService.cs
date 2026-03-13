using GV23_Notice.Models.DTOs;

namespace GV23_Notice.Services.Search
{
    public interface INoticeSearchService
    {
        Task<List<string>> GetTownshipsAsync(CancellationToken ct);
        Task<List<string>> GetSchemesByTownshipAsync(string town, CancellationToken ct);
        Task<NoticeSearchResult> SearchByObjectionNoAsync(string term, CancellationToken ct);
        Task<NoticeSearchResult> SearchByAppealNoAsync(string term, CancellationToken ct);
        Task<NoticeSearchResult> SearchByPropertyDescAsync(string? township, string? scheme, string? erfNo, string? address, string? unitNo, CancellationToken ct);
        Task<byte[]> BuildZipAsync(IEnumerable<string> filePaths, CancellationToken ct);
        Task<string?> GetPdfPathAsync(int runLogId, CancellationToken ct);
        Task LogDownloadAsync(string downloadedBy, NoticeSearchResult result, string zipFileName, int fileCount, CancellationToken ct);
    }

}

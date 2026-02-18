namespace GV23_Notice.Services.Email
{
    public interface INoticeEmailArchiveService
    {
        Task<string> SaveAsync(
            int rollId,
            DataDomain domain,            // ✅ Objection | Appeal
            string rollShortCode,
            string notice,
            int version,
            string category,              // "Approval" | "Correction"
            string fileStem,
            string subject,
            string bodyHtml,
            object meta,
            CancellationToken ct = default);
    }
}

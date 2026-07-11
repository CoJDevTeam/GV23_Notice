namespace GV23_Notice.Services.Stats
{
    public interface INoticeStatsEmailService
    {
        Task SendStatsEmailAsync(
            string toEmails,
            string? ccEmails,
            string subject,
            string htmlBody,
            string attachmentPath,
            CancellationToken ct,
            string? fromAddress = null,
            string? fromName = null);
    }
}
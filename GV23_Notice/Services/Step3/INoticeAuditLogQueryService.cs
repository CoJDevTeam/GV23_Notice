using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Step3
{
    public interface INoticeAuditLogQueryService
    {
        Task<List<AuditLogRowVm>> GetStep2AuditLogsAsync(int rollId, string version, Guid workflowKey, CancellationToken ct);
        Task<(string? EmlPath, string? PdfPath, string? RecipientEmail, DateTime CreatedAtUtc)> GetLatestStep2AuditEmailAsync(int rollId, string version, Guid workflowKey, CancellationToken ct);
    }
}

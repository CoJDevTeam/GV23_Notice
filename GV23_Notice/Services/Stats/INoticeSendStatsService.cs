using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Stats
{
    public interface INoticeSendStatsService
    {
        Task<NoticeSendStatsVm> BuildStatsAsync(
            Guid workflowKey,
            CancellationToken ct);

        Task<string> GenerateExcelAsync(
            Guid workflowKey,
            string generatedBy,
            CancellationToken ct);

        Task SendStatsEmailAsync(
            Guid workflowKey,
            string toEmails,
            string? ccEmails,
            string sentBy,
            CancellationToken ct);

      
    }
}
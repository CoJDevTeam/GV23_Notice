using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Step3
{
    public interface ICorrectionTicketQueryService
    {
        Task<List<CorrectionTicketRowVm>> ListBySettingsAsync(int settingsId, CancellationToken ct);
    }
}

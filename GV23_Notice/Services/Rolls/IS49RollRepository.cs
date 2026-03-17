using GV23_Notice.Models.DTOs;

namespace GV23_Notice.Services.Rolls
{
    public interface IS49RollRepository
    {
        Task<List<string>> PickNextPremiseIdsAsync(int rollId, int top, CancellationToken ct);

        Task<(List<S49RollRowDto> rows, SapContactDto? contact)> LoadPremiseAsync(
            int rollId,
            string premiseId,
            CancellationToken ct);

        /// <summary>Mark 'P' — PDF generation has started.</summary>
        Task MarkPrintingAsync(int rollId, string premiseId, CancellationToken ct);

        /// <summary>Mark 'NP' — PDF generation failed.</summary>
        Task MarkPrintFailedAsync(int rollId, string premiseId, CancellationToken ct);

        /// <summary>Mark 'Y' — email sent to ratepayer.</summary>
        Task MarkEmailSentAsync(int rollId, string premiseId, CancellationToken ct);

        /// <summary>Mark 'N' — email send failed (PDF exists).</summary>
        Task MarkEmailFailedAsync(int rollId, string premiseId, CancellationToken ct);
    }
}

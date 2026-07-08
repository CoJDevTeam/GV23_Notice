using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Services.Corrections
{
    public interface INoticeCorrectionSourceService
    {
        Task<CorrectionPreviewVm?> SearchAsync(
            int rollId,
            NoticeKind sourceNotice,
            NoticeKind printNotice,
            string referenceType,
            string referenceNo,
            CancellationToken ct);
    }
}

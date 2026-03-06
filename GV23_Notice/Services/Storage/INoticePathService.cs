using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Services.Storage
{
    public interface INoticePathService
    {
        string GetRootPath(RollRegistry roll, NoticeKind notice);

        /// <summary>
        /// Original path builder (no batch folder).
        /// </summary>
        string BuildPdfPath(
            RollRegistry roll,
            NoticeKind notice,
            string keyNo,
            string propertyDesc,
            string? copyRole);

        /// <summary>
        /// Batch print path:
        /// {root}\{Roll}_{Notice}\Batches\{batchName}\{propertyDesc}_{Notice}.pdf
        /// </summary>
        string BuildBatchPdfPath(
            RollRegistry roll,
            NoticeKind notice,
            string batchName,
            string propertyDesc,
            string? copyRole = null);
        /// <summary>
        /// Batch email save path:
        /// {root}\{Roll}_{Notice}\Batches\{batchName}_Emails\{propertyDesc}.eml
        /// </summary>
        string BuildBatchEmlPath(
            RollRegistry roll,
            NoticeKind notice,
            string batchName,
            string propertyDesc);
    }
}
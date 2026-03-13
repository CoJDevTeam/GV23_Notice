using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Services.Storage
{
    public interface INoticePathService
    {
        string GetRootPath(RollRegistry roll, NoticeKind notice);

        /// <summary>Original path builder (no batch folder).</summary>
        string BuildPdfPath(
            RollRegistry roll,
            NoticeKind notice,
            string keyNo,
            string propertyDesc,
            string? copyRole);

        /// <summary>
        /// S52-specific path:
        /// {root}\{Appeal_No}\Section 52 Review\{Appeal_No}_{PropertyDesc}_S52.pdf  (isReview=true)
        /// {root}\{Appeal_No}\Appeal Decision\{Appeal_No}_{PropertyDesc}_AD.pdf     (isReview=false)
        /// </summary>
        string BuildS52PdfPath(
            RollRegistry roll,
            string appealNo,
            string propertyDesc,
            bool isReview);

        /// <summary>
        /// S52-specific .eml path — same folder as PDF, .eml extension.
        /// {root}\{Appeal_No}\Section 52 Review\{Appeal_No}_{PropertyDesc}_S52.eml
        /// {root}\{Appeal_No}\Appeal Decision\{Appeal_No}_{PropertyDesc}_AD.eml
        /// </summary>
        string BuildS52EmlPath(
            RollRegistry roll,
            string appealNo,
            string propertyDesc,
            bool isReview);

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

        /// <summary>
        /// S51 .eml save path — sits alongside the PDF in the ObjectionNo folder:
        /// {root}\{ObjectionNo}\Section 51 Notice\{ObjectionNo}_{PropertyDesc}.eml
        /// </summary>
        string BuildS51EmlPath(
            RollRegistry roll,
            string objectionNo,
            string propertyDesc);

        /// <summary>
        /// S53 PDF path.
        /// Folder : {root}\{ObjectionNo}\Section 53 MVD\
        /// Files  :
        ///   Owner / Representative / Third_Party → {ObjectionNo}_{PropertyDesc}_MVD.pdf
        ///   Owner_Rep                            → {ObjectionNo}_{PropertyDesc}_Owner_Rep_MVD.pdf
        ///   Owner_Third_Party                    → {ObjectionNo}_{PropertyDesc}_Owner_Third_Party_MVD.pdf
        /// </summary>
        string BuildS53PdfPath(
            RollRegistry roll,
            string objectionNo,
            string propertyDesc,
            string? objectorType);

        /// <summary>
        /// S53 .eml path — same folder as the PDF, .eml extension.
        /// </summary>
        string BuildS53EmlPath(
            RollRegistry roll,
            string objectionNo,
            string propertyDesc,
            string? objectorType);

        /// <summary>
        /// Dear Johnny PDF path:
        /// {root}\{ObjectionNo}\Dear Johnny Notice\{ObjectionNo}_{PropertyDesc}_DJ.pdf
        /// </summary>
        string BuildDjPdfPath(RollRegistry roll, string objectionNo, string propertyDesc);

        /// <summary>
        /// Dear Johnny .eml path — same folder as PDF, .eml extension.
        /// </summary>
        string BuildDjEmlPath(RollRegistry roll, string objectionNo, string propertyDesc);

        /// <summary>
        /// Invalid Notice PDF path:
        /// isOmission=false → {root}\{ObjectionNo}\Invalid Notice\{ObjectionNo}_{PropertyDesc}_IO.pdf
        /// isOmission=true  → {root}\{ObjectionNo}\Invalid Notice\{ObjectionNo}_{PropertyDesc}_IOM.pdf
        /// </summary>
        string BuildInPdfPath(RollRegistry roll, string objectionNo, string propertyDesc, bool isOmission);

        /// <summary>
        /// Invalid Notice .eml path — same folder as PDF, .eml extension.
        /// </summary>
        string BuildInEmlPath(RollRegistry roll, string objectionNo, string propertyDesc, bool isOmission);
    }
}
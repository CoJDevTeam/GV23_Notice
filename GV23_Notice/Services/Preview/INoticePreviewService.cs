namespace GV23_Notice.Services.Preview
{
    using global::GV23_Notice.Models.DTOs;
  

    namespace GV23_Notice.Services.Notices
    {
        /// <summary>
        /// Builds Step2 preview artifacts (PDF + Email) from APPROVED Step1 settings and real DB snapshot data.
        /// </summary>
        public interface INoticePreviewService
        {
            /// <summary>
            /// Build a preview package for Step2.
            /// - settingsId: the approved NoticeSettings record (Step1 snapshot)
            /// - variant: which preview flavour (e.g. S52 Review vs Appeal, Invalid Omission vs Invalid Objection)
            /// - mode: single vs split pdf, single vs multi email preview mode
            /// - appealNo: required only for Section 52 previews (review/appeal)
            /// </summary>
            Task<NoticePreviewResult> BuildPreviewAsync(
                int settingsId,
                PreviewVariant variant,
                PreviewMode mode,
                string? appealNo,
                CancellationToken ct);
        }
    }

}

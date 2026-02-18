using GV23_Notice.Services.Notices.Section53.COJ_Notice_2026.Models.ViewModels.Section53;

namespace GV23_Notice.Services.Notices.Section53
{
   

        public interface ISection53PdfService
        {
            // Real (DB rows)
            byte[] BuildNoticePdf(Section53MvdRow row, DateOnly letterDate);
            byte[] BuildNoticePdf(IReadOnlyList<Section53MvdRow> rows, DateOnly letterDate);

            // Preview (dummy)
            byte[] BuildPreviewPdf(Section53MvdRow dummyRow, DateOnly letterDate);
        }
    }



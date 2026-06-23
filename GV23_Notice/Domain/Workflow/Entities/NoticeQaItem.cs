

using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public sealed class NoticeQaItem
    {
        public int Id { get; set; }

        public int NoticeQaRunId { get; set; }
        public NoticeQaRun NoticeQaRun { get; set; } = null!;

        public int NoticeRunLogId { get; set; }
        public NoticeRunLog NoticeRunLog { get; set; } = null!;

        [MaxLength(80)]
        public string? ObjectionNo { get; set; }

        [MaxLength(80)]
        public string? PremiseId { get; set; }

        [MaxLength(30)]
        public string? PropertyType { get; set; }

        [MaxLength(512)]
        public string? PropertyDesc { get; set; }

        [MaxLength(260)]
        public string? PdfPath { get; set; }

        [MaxLength(200)]
        public string? NewCategoryMvd { get; set; }

        [MaxLength(200)]
        public string? New2CategoryMvd { get; set; }

        [MaxLength(200)]
        public string? New3CategoryMvd { get; set; }

        [MaxLength(200)]
        public string? ExpectedCategory { get; set; }

        public bool IsCategoryValid { get; set; }

        [MaxLength(50)]
        public string QaStatus { get; set; } = "Pending";
        // Pending | Passed | Failed

        [MaxLength(2000)]
        public string? QaComment { get; set; }
    }
}
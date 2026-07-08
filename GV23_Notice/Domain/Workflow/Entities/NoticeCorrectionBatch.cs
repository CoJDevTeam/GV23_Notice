namespace GV23_Notice.Domain.Workflow.Entities
{
    public class NoticeCorrectionBatch
    {
        public int Id { get; set; }

        public string CorrectionBatchName { get; set; } = "";

        public int RollId { get; set; }
        public string? RollShortCode { get; set; }
        public string? SourceDb { get; set; }

        public string NoticeKind { get; set; } = "";
        public string? NoticeSubKind { get; set; }
        public string? SourceNoticeKind { get; set; }
        public string? PrintNoticeKind { get; set; }
        public string? PrintNoticeTitle { get; set; }

        public string ReferenceType { get; set; } = "";
        public string ReferenceNo { get; set; } = "";

        public string? CorrectionReason { get; set; }

        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        public string? PrintedBy { get; set; }
        public DateTime? PrintedAt { get; set; }

        public string? SentBy { get; set; }
        public DateTime? SentAt { get; set; }

        public string Status { get; set; } = "Batch-Created";

        public ICollection<NoticeCorrectionItem> Items { get; set; } = new List<NoticeCorrectionItem>();
    }
}

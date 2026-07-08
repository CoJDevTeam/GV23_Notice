namespace GV23_Notice.Domain.Workflow.Entities
{
    public class NoticeCorrectionEmailTemplate
    {
        public int Id { get; set; }

        public string NoticeKind { get; set; } = "";
        public string? NoticeSubKind { get; set; }

        public string TemplateName { get; set; } = "";
        public string SubjectTemplate { get; set; } = "";
        public string BodyTemplate { get; set; } = "";

        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }

        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CcTemplate { get; set; }
    }
}

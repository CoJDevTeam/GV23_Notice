using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.DTOs
{
    public sealed class NoticeFileResult
    {
        public int RunLogId { get; set; }       // 0 when sourced from roll DB (no RunLog)
        public string ObjectionNo { get; set; } = "";
        public string? AppealNo { get; set; }
        public string PropertyDesc { get; set; } = "";
        public string NoticeType { get; set; } = ""; // derived from folder name
        public string ObjectorType { get; set; } = "";
        public string? PdfPath { get; set; }
        public string? EmlPath { get; set; }
        public bool PdfExists { get; set; }
        public bool EmlExists { get; set; }
        public string Status { get; set; } = ""; // raw string from DB
        public string ObjectionStatus { get; set; } = ""; // objection_Status from Obj_Property_Info
        public string RollName { get; set; } = "";
        public string TableSource { get; set; } = ""; // Objection | Appeal
    }

    public sealed class NoticeSearchResult
    {
        public string SearchMode { get; set; } = "";
        public string SearchTerm { get; set; } = "";
        public List<NoticeFileResult> Files { get; set; } = new();
        public int TotalFiles => Files.Count;
        public int PdfCount => Files.Count(f => f.PdfExists);
        public int EmlCount => Files.Count(f => f.EmlExists);
    }

}

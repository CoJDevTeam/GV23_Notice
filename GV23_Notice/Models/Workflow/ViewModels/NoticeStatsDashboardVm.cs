using GV23_Notice.Domain.Workflow;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GV23_Notice.Models.Stats
{
    public sealed class NoticeStatsDashboardVm
    {
        public NoticeStatsFilterVm Filter { get; set; } = new();

        public List<SelectListItem> RollOptions { get; set; } = new();
        public List<SelectListItem> NoticeOptions { get; set; } = new();
        public List<SelectListItem> StatusOptions { get; set; } = new();

        public int TotalBatches { get; set; }
        public int TotalRecords { get; set; }
        public int TotalPrinted { get; set; }
        public int TotalSent { get; set; }
        public int TotalFailed { get; set; }
        public int TotalNoEmail { get; set; }
        public int TotalGenerated { get; set; }

        public List<NoticeStatsBatchRowVm> Batches { get; set; } = new();
    }

    public sealed class NoticeStatsFilterVm
    {
        public int? RollId { get; set; }
        public NoticeKind? Notice { get; set; }
        public RunStatus? Status { get; set; }

        public string? BatchName { get; set; }
        public string? ReferenceNo { get; set; }
        public string? RecipientEmail { get; set; }
        public string? SentBy { get; set; }

        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
    }

    public sealed class NoticeStatsBatchRowVm
    {
        public Guid WorkflowKey { get; set; }

        public int BatchId { get; set; }
        public string BatchName { get; set; } = "";

        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public string VersionText { get; set; } = "";

        public DateTime BatchDate { get; set; }

        public int TotalRecords { get; set; }
        public int Generated { get; set; }
        public int Printed { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int NoEmail { get; set; }

        public DateTime? LastSentAtUtc { get; set; }
        public string? SentBy { get; set; }
    }

    public sealed class NoticeStatsDetailsVm
    {
        public Guid WorkflowKey { get; set; }
        public int BatchId { get; set; }

        public string BatchName { get; set; } = "";
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public string VersionText { get; set; } = "";

        public List<NoticeStatsDetailRowVm> Rows { get; set; } = new();
    }

    public sealed class NoticeStatsDetailRowVm
    {
        public string? ObjectionNo { get; set; }
        public string? AppealNo { get; set; }
        public string? PremiseId { get; set; }

        public string? PropertyDesc { get; set; }
        public string? RecipientName { get; set; }
        public string? RecipientEmail { get; set; }

        public RunStatus Status { get; set; }
        public string? ErrorMessage { get; set; }

        public string? PdfPath { get; set; }
        public string? EmlPath { get; set; }

        public DateTime? SentAtUtc { get; set; }
        public string? SentBy { get; set; }
    }
}   
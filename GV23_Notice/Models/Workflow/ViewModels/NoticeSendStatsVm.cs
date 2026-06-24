using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class NoticeSendStatsVm
    {
        public Guid WorkflowKey { get; set; }

        public int SettingsId { get; set; }

        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public string VersionText { get; set; } = "";

        public int TotalBatches { get; set; }
        public int TotalRecords { get; set; }

        public int TotalPrinted { get; set; }
        public int TotalSent { get; set; }
        public int TotalFailed { get; set; }
        public int TotalNoEmail { get; set; }

        public string? SentBy { get; set; }
        public DateTime? LastSentAtUtc { get; set; }

        public bool QaRequired { get; set; }
        public bool QaApproved { get; set; }
        public string? QaApprovedBy { get; set; }
        public DateTime? QaApprovedAtUtc { get; set; }

        public string? LastStatsExcelPath { get; set; }
        public string? DefaultToEmails { get; set; }

        public string? DefaultCcEmails { get; set; }

        public List<NoticeSendStatsBatchVm> Batches { get; set; } = new();
        public List<NoticeSendStatsRowVm> Rows { get; set; } = new();
    }

    public sealed class NoticeSendStatsBatchVm
    {
        public int BatchId { get; set; }
        public string BatchName { get; set; } = "";
        public DateTime BatchDate { get; set; }

        public int TotalRecords { get; set; }
        public int Printed { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int NoEmail { get; set; }
    }

    public sealed class NoticeSendStatsRowVm
    {
        public string? BatchName { get; set; }

        public string? ObjectionNo { get; set; }
        public string? AppealNo { get; set; }
        public string? PremiseId { get; set; }

        public string? PropertyDesc { get; set; }

        public string? RecipientName { get; set; }
        public string? RecipientEmail { get; set; }

        public string Status { get; set; } = "";

        public string? ErrorMessage { get; set; }

        public string? PdfPath { get; set; }

        public string? EmlPath { get; set; }

        public DateTime? SentAtUtc { get; set; }
        public string? sentBy { get; set; }


    }
}
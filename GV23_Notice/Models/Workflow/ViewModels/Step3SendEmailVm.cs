using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class Step3SendEmailVm
    {
        public Guid WorkflowKey { get; set; }
        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";
        public NoticeKind Notice { get; set; }
        public string VersionText { get; set; } = "";

        // Workflow summary (readonly)
        public DateTime? LetterDate { get; set; }
        public string? FinancialYearsText { get; set; }
        public DateTime? ObjectionStartDate { get; set; }
        public DateTime? ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }
        public string? SignaturePath { get; set; }

        // Email stats
        public int TotalBatches { get; set; }
        public int TotalPrinted { get; set; }   // eligible to send
        public int TotalSent { get; set; }   // already sent
        public int MaxEmailsPerSend { get; set; } = 2000;

        // S52 flags — used to show the correct .eml save path in the view
        public bool IsS52 { get; set; }
        public bool S52IsReview { get; set; }

        public List<Step3EmailBatchRowVm> Batches { get; set; } = new();
    }

    public sealed class Step3EmailBatchRowVm
    {
        public int BatchId { get; set; }
        public string BatchName { get; set; } = "";
        public DateTime BatchDate { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }

        // Counts
        public int PrintedCount { get; set; }   // ready to email
        public int SentCount { get; set; }   // already sent
        public int FailedCount { get; set; }
        public int NoEmailCount { get; set; }

        public bool IsFullySent => PrintedCount == 0 && SentCount > 0;
        public bool HasReadyToSend => PrintedCount > 0;
    }
}
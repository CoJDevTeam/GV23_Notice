using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class WorkflowStep3KickoffVm
    {
        public int SettingsId { get; set; }
        public Guid ApprovalKey { get; set; }

        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public BatchMode Mode { get; set; }
        public int Version { get; set; }

        // Approval metadata
        public bool IsApproved { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }

        // Step1 snapshot (read-only)
        public DateTime LetterDate { get; set; }
        public DateTime? ObjectionStartDate { get; set; }
        public DateTime? ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }
        public string? FinancialYearsText { get; set; }
        public string? SignaturePath { get; set; }

        // ✅ NEW: Step2-like preview fields
        public string RecipientName { get; set; } = "";
        public string RecipientEmail { get; set; } = "";
        public string EmailSubject { get; set; } = "";
        public string EmailBodyHtml { get; set; } = "";
        public string PdfUrl { get; set; } = "";

        // ✅ Keep UI selection (optional, but nice)
        public string SelectedVariant { get; set; } = "Default";
        public string SelectedMode { get; set; } = "single";
        public string? AppealNo { get; set; } // for S52 only

      
        // Real DB preview (one premise sample)
        public string? SamplePremiseId { get; set; }
        public string? SampleEmail { get; set; }
        public int SampleRowCount { get; set; }
        public bool SampleIsSplit { get; set; }

        // Preview URLs (generated live)
        public string PdfPreviewUrl { get; set; } = "";
        public string EmailPreviewHtml { get; set; } = "";
    }
}

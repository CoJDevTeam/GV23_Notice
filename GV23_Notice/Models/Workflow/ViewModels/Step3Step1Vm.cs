using GV23_Notice.Domain.Workflow;
using GV23_Notice.Services.Preview;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class Step3Step1Vm
    {
        // Key context
        public Guid WorkflowKey { get; set; }
        public int SettingsId { get; set; }
        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";
        public NoticeKind Notice { get; set; }
        public PreviewMode Mode { get; set; }
        public int Version { get; set; }

        // Step1: confirmed settings
        public DateTime LetterDate { get; set; }
        public DateTime? CityManagerSignDate { get; set; }
        public DateTime? ObjectionStartDate { get; set; }
        public DateTime? ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }
        public DateTime? EvidenceCloseDate { get; set; }
        public DateTime? BulkFromDate { get; set; }
        public DateTime? BulkToDate { get; set; }
        public DateTime? BatchDate { get; set; }
        public DateTime? AppealCloseDate { get; set; }

        public string? PortalUrl { get; set; }
        public string? EnquiriesLine { get; set; }

        // Step1 approval info (you already have settings.IsApproved)
        public bool Step1Approved { get; set; }
        public string? Step1ApprovedBy { get; set; }
        public DateTime? Step1ApprovedAtUtc { get; set; }

        // Step2 status
        public bool Step2Approved { get; set; }
        public string? Step2ApprovedBy { get; set; }
        public DateTime? Step2ApprovedAtUtc { get; set; }

        public bool Step2CorrectionRequested { get; set; }
        public string? Step2CorrectionRequestedBy { get; set; }
        public DateTime? Step2CorrectionRequestedAtUtc { get; set; }
        public string? Step2CorrectionReason { get; set; }

        // Last workflow email shown on view (approval or correction)
        public WorkflowEmailVm? LatestWorkflowEmail { get; set; }

        // Tickets (correction requests)
        public List<CorrectionTicketRowVm> Tickets { get; set; } = new();

        // Audit logs (runlogs are your audit spine)
        public List<AuditLogRowVm> AuditLogs { get; set; } = new();


    
        public string VersionText { get; set; } = "";


    }

    public sealed class WorkflowEmailVm
    {
        public string To { get; set; } = "";
        public string Subject { get; set; } = "";
        public string BodyHtml { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public string? EmlPath { get; set; }
    }

    public sealed class CorrectionTicketRowVm
    {
        public int Id { get; set; }
        public TicketStatus Status { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string RequestedBy { get; set; } = "";
        public DateTime RequestedAtUtc { get; set; }
        public string RequestComment { get; set; } = "";
        public string? ResolvedBy { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
    }

    public sealed class AuditLogRowVm
    {
        public DateTime AtUtc { get; set; }
        public string Status { get; set; } = "";    // Generated/Sent/Failed etc.
        public string To { get; set; } = "";
        public string? PdfPath { get; set; }
        public string? EmlPath { get; set; }
        public string? Error { get; set; }
    }
}

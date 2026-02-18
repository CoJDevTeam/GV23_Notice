using System.ComponentModel.DataAnnotations;

namespace GV23_Notice.Domain.Workflow.Entities
{
    public class CorrectionTicket
    {
        public int Id { get; set; }

        public int NoticeSettingsId { get; set; }
        public NoticeSettings NoticeSettings { get; set; } = null!;

        [MaxLength(200)]
        public string Title { get; set; } = "Template correction required";

        [MaxLength(4000)]
        public string Description { get; set; } = "";

        public TicketStatus Status { get; set; } = TicketStatus.Open;

        [MaxLength(256)]
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Notification details (testing: single recipient)
        [MaxLength(500)]
        public string NotifiedTo { get; set; } = "JabulaniSib@joburg.org.za";
        public DateTime? NotifiedAtUtc { get; set; }

        [MaxLength(2000)]
        public string? LastEmailError { get; set; }


       

        public int RollId { get; set; }              // convenience filter
        public NoticeKind Notice { get; set; }       // convenience filter
        public int Version { get; set; }             // snapshot of settings version

      

        // Admin request
        public string RequestedBy { get; set; } = "";
        public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
        public string RequestComment { get; set; } = "";

        // Optional evidence upload path (if you later add it)
        public string? EvidencePath { get; set; }

        // Email notification tracking
        public bool EmailSent { get; set; }
        public DateTime? EmailSentAtUtc { get; set; }
        public string? EmailTo { get; set; }         // e.g., JabulaniSib@joburg.org.za or multi recipients

        // Data team resolution
        public string? ResolvedBy { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
        public string? ResolutionNote { get; set; }
    }
}

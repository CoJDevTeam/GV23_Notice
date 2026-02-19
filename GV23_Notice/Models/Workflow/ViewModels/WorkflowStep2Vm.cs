using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class WorkflowStep2Vm
    {
        public int SettingsId { get; set; }

        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public BatchMode Mode { get; set; }
        public int Version { get; set; }

        // Step1 dates summary
        public DateTime LetterDate { get; set; }
        public DateTime? BatchDate { get; set; }
        public DateTime? EvidenceCloseDate { get; set; }
        public DateTime? AppealCloseDate { get; set; }
        public DateTime? ObjectionStartDate { get; set; }
        public DateTime? ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }

        // Email preview (dummy template rendering)
        public string EmailSubject { get; set; } = "";
        public string EmailBodyHtml { get; set; } = "";

        // PDF preview URL (iframe)
        public string PdfPreviewUrl { get; set; } = "";

        // status flags
        public bool Step1Approved { get; set; }
        public bool Step2Approved { get; set; }
        public string? Step2ApprovedBy { get; set; }
        public DateTime? Step2ApprovedAtUtc { get; set; }

        // correction modal
        public string? CorrectionComment { get; set; }

        // for display
        public string RecipientName { get; set; } = "Notice Sample";
        public string RecipientEmail { get; set; } = "NoticeSample@joburg.org.za";
        public string AddressLine { get; set; } = "66 Jorissen Place, Jorissen Street, Braamfontein";
        public string SampleObjectionNo { get; set; } = "";
        public string SampleAppealNo { get; set; } = "";

        public string AppealNo { get; set; } = "";

        public string? PortalUrl { get; set; } 
         

        public string? EnquiriesLine { get; set; }


        public string? FinancialYearsText { get; set; }

        // UI selections (tabs)
        public string SelectedVariant { get; set; } = ""; // e.g. "InvalidOmission"
        public string SelectedMode { get; set; } = "single"; // single | multi

     

        public string PdfUrl { get; set; } = "";

     
        public DateTime? CityManagerSignDate { get; set; }

     

        public DateTime? BulkFromDate { get; set; }
        public DateTime? BulkToDate { get; set; }

       

        public DateTime? ExtractionDate { get; set; }
        public int? ExtractPeriodDays { get; set; }
        public DateTime? ReviewOpenDate { get; set; }
        public DateTime? ReviewCloseDate { get; set; }

        public string? SignaturePath { get; set; }
        public bool IsConfirmed { get; set; }
        public bool IsApproved { get; set; }

    }
}

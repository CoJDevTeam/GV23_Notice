namespace GV23_Notice.Domain.Workflow.Entities
{
    public class ClaThirdPartyApplicationNotice
    {
        public int Id { get; set; }

        // Workflow
        public int? NoticeSettingsId { get; set; }
        public int? RollId { get; set; }

        public string? RollShortCode { get; set; }
        public string? ValuationPeriod { get; set; }

        public DateTime? LetterDate { get; set; }
        public DateTime? RepresentationCloseDate { get; set; }

        // References
        public string? ClaNumber { get; set; }
        public string? ObjectionNumber { get; set; }
        public string? PremiseId { get; set; }

        // Property
        public string? PropertyDescription { get; set; }

        // Owner
        public string? OwnerName { get; set; }
        public string? OwnerEmail { get; set; }

        public string? OwnerAddress1 { get; set; }
        public string? OwnerAddress2 { get; set; }
        public string? OwnerAddress3 { get; set; }
        public string? OwnerAddress4 { get; set; }
        public string? OwnerAddress5 { get; set; }

        // Third party
        public string? ThirdPartyName { get; set; }
        public string? ThirdPartyEmail { get; set; }

        public string? ThirdPartyAddress1 { get; set; }
        public string? ThirdPartyAddress2 { get; set; }
        public string? ThirdPartyAddress3 { get; set; }
        public string? ThirdPartyAddress4 { get; set; }
        public string? ThirdPartyAddress5 { get; set; }

        // Assigned Admin
        public string? AdminName { get; set; }
        public string? AdminEmail { get; set; }

        // General Valuation Roll
        public decimal? RollMarketValue1 { get; set; }
        public string? RollCategory1 { get; set; }
        public decimal? RollExtent1 { get; set; }

        public decimal? RollMarketValue2 { get; set; }
        public string? RollCategory2 { get; set; }
        public decimal? RollExtent2 { get; set; }

        public decimal? RollMarketValue3 { get; set; }
        public string? RollCategory3 { get; set; }
        public decimal? RollExtent3 { get; set; }

        // Objection outcome
        public decimal? ObjectionOutcomeMarketValue1 { get; set; }
        public string? ObjectionOutcomeCategory1 { get; set; }
        public decimal? ObjectionOutcomeExtent1 { get; set; }

        public decimal? ObjectionOutcomeMarketValue2 { get; set; }
        public string? ObjectionOutcomeCategory2 { get; set; }
        public decimal? ObjectionOutcomeExtent2 { get; set; }

        public decimal? ObjectionOutcomeMarketValue3 { get; set; }
        public string? ObjectionOutcomeCategory3 { get; set; }
        public decimal? ObjectionOutcomeExtent3 { get; set; }

        // CLA request
        public decimal? ClaRequestedMarketValue1 { get; set; }
        public string? ClaRequestedCategory1 { get; set; }
        public decimal? ClaRequestedExtent1 { get; set; }

        public decimal? ClaRequestedMarketValue2 { get; set; }
        public string? ClaRequestedCategory2 { get; set; }
        public decimal? ClaRequestedExtent2 { get; set; }

        public decimal? ClaRequestedMarketValue3 { get; set; }
        public string? ClaRequestedCategory3 { get; set; }
        public decimal? ClaRequestedExtent3 { get; set; }

        public bool IsMultipurpose { get; set; }

        // Generated notice
        public string? PdfPath { get; set; }
        public bool PdfExists { get; set; }

        public DateTime? PrintedAtUtc { get; set; }
        public string? PrintedBy { get; set; }

        // Appeal pack
        public string? AppealPackPath { get; set; }
        public string? AppealPackFileName { get; set; }
        public bool AppealPackExists { get; set; }

        // Email
        public string? EmailTo { get; set; }
        public string? EmailCc { get; set; }
        public string? EmailSubject { get; set; }
        public string? EmailBody { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public string? SentBy { get; set; }
        public string? EmailError { get; set; }

        public string Status { get; set; } = "Imported";

        public bool IsActive { get; set; } = true;

        public string? CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }

        // Navigation property
        public NoticeSettings? NoticeSettings { get; set; }
    }
}
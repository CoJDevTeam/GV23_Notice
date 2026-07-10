namespace GV23_Notice.Domain.Workflow.Entities
{
    public class ThirdPartyAppealApplicationNotice
    {
        public int Id { get; set; }

        public int? NoticeSettingsId { get; set; }
        public int? RollId { get; set; }
        public string? RollShortCode { get; set; }
        public string? ValuationPeriod { get; set; }

        public string Appeal_No { get; set; } = "";
        public string? Objection_No { get; set; }
        public string? Premise_ID { get; set; }
        public string? Valuation_Key { get; set; }
        public string? Unit_Key { get; set; }
        public string? Property_ID { get; set; }

        public string? Appeal_Type { get; set; }
        public string? Appeal_Status { get; set; }
        public string? Property_Type { get; set; }
        public string? Township { get; set; }
        public string? Property_Description { get; set; }

        public string? OwnerName { get; set; }
        public string? OwnerEmail { get; set; }
        public string? OwnerCell { get; set; }
        public string? OwnerAddress1 { get; set; }
        public string? OwnerAddress2 { get; set; }
        public string? OwnerAddress3 { get; set; }
        public string? OwnerAddress4 { get; set; }
        public string? OwnerAddress5 { get; set; }

        public string? ThirdPartyName { get; set; }
        public string? ThirdPartyEmail { get; set; }
        public string? ThirdPartyCell { get; set; }
        public string? ThirdPartyAddress1 { get; set; }
        public string? ThirdPartyAddress2 { get; set; }
        public string? ThirdPartyAddress3 { get; set; }
        public string? ThirdPartyAddress4 { get; set; }
        public string? ThirdPartyAddress5 { get; set; }

        public string? AdminName { get; set; }
        public string? AdminEmail { get; set; }

        public string? RollMarketValue1 { get; set; }
        public string? RollMarketValue2 { get; set; }
        public string? RollMarketValue3 { get; set; }

        public string? RollCategory1 { get; set; }
        public string? RollCategory2 { get; set; }
        public string? RollCategory3 { get; set; }

        public string? RollExtent1 { get; set; }
        public string? RollExtent2 { get; set; }
        public string? RollExtent3 { get; set; }

        public string? ObjectionOutcomeMarketValue1 { get; set; }
        public string? ObjectionOutcomeMarketValue2 { get; set; }
        public string? ObjectionOutcomeMarketValue3 { get; set; }

        public string? ObjectionOutcomeCategory1 { get; set; }
        public string? ObjectionOutcomeCategory2 { get; set; }
        public string? ObjectionOutcomeCategory3 { get; set; }

        public string? ObjectionOutcomeExtent1 { get; set; }
        public string? ObjectionOutcomeExtent2 { get; set; }
        public string? ObjectionOutcomeExtent3 { get; set; }

        public string? AppellantRequestMarketValue1 { get; set; }
        public string? AppellantRequestMarketValue2 { get; set; }
        public string? AppellantRequestMarketValue3 { get; set; }

        public string? AppellantRequestCategory1 { get; set; }
        public string? AppellantRequestCategory2 { get; set; }
        public string? AppellantRequestCategory3 { get; set; }

        public string? AppellantRequestExtent1 { get; set; }
        public string? AppellantRequestExtent2 { get; set; }
        public string? AppellantRequestExtent3 { get; set; }

        public string? AppReason { get; set; }

        public DateTime? DateAdded { get; set; }
        public DateTime? LetterDate { get; set; }
        public DateTime? ResponseDueDate { get; set; }
        public DateTime? ScheduleDate { get; set; }
        public DateTime? HearingDate { get; set; }

        public string? PdfPath { get; set; }
        public string? AppealPackFolderPath { get; set; }
        public string? AppealPackZipPath { get; set; }

        public string? EmailSubject { get; set; }
        public string? EmailBody { get; set; }
        public string? EmailTo { get; set; }
        public string? EmailCc { get; set; }
        public string? EmlPath { get; set; }

        public string Status { get; set; } = "Pending";

        public DateTime? PreviewApprovedAt { get; set; }
        public string? PreviewApprovedBy { get; set; }

        public DateTime? PrintedAt { get; set; }
        public string? PrintedBy { get; set; }

        public DateTime? SentAt { get; set; }
        public string? SentBy { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? CreatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public string? VAB { get; set; }
        public string? Valuer { get; set; }
        public string? Valuer_Email { get; set; }
        public string? Objector_Status { get; set; }
        public bool IsMultipurpose =>
            HasValue(AppellantRequestMarketValue2) ||
            HasValue(AppellantRequestMarketValue3) ||
            HasValue(AppellantRequestCategory2) ||
            HasValue(AppellantRequestCategory3) ||
            HasValue(AppellantRequestExtent2) ||
            HasValue(AppellantRequestExtent3) ||
            string.Equals(Property_Type, "Multi", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Property_Type, "Multipurpose", StringComparison.OrdinalIgnoreCase);

        private static bool HasValue(string? value)
            => !string.IsNullOrWhiteSpace(value);
    }
}

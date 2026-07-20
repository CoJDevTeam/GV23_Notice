using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GV23_Notice.Domain.Workflow.Entities
{
    [Table("ClaThirdPartyApplicationNotices")]
    public sealed class ClaThirdPartyApplicationNotice
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Column("NoticeSettingsId")]
        public int? NoticeSettingsId { get; set; }

        [Column("WorkflowKey")]
        public Guid? WorkflowKey { get; set; }

        [Column("ApprovalKey")]
        public Guid? ApprovalKey { get; set; }

        [Column("RollId")]
        public int? RollId { get; set; }

        [Column("RollShortCode")]
        public string? RollShortCode { get; set; }

        [Column("ValuationPeriod")]
        public string? ValuationPeriod { get; set; }

        [Column("Version")]
        public int Version { get; set; }

        // CLA references
        [Column("CLA_No")]
        public string ClaNumber { get; set; } = string.Empty;

        [Column("Objection_No")]
        public string? ObjectionNumber { get; set; }

        [Column("Premise_ID")]
        public string PremiseId { get; set; } = string.Empty;

        [Column("Unit_Key")]
        public string? UnitKey { get; set; }

        [Column("Valuation_Key")]
        public string? ValuationKey { get; set; }

        // Property
        [Column("Property_Description")]
        public string? PropertyDescription { get; set; }

        [Column("Property_Address")]
        public string? PropertyAddress { get; set; }

        // GV values
        [Column("GV_Market_Value")]
        public decimal? RollMarketValue1 { get; set; }

        [Column("GV_Category")]
        public string? RollCategory1 { get; set; }

        [Column("GV_Extent")]
        public decimal? RollExtent1 { get; set; }

        // Objection outcome
        [Column("Objection_Market_Value")]
        public decimal? ObjectionOutcomeMarketValue1 { get; set; }

        [Column("Objection_Category")]
        public string? ObjectionOutcomeCategory1 { get; set; }

        [Column("Objection_Extent")]
        public decimal? ObjectionOutcomeExtent1 { get; set; }

        // CLA request
        [Column("CLA_Market_Value")]
        public decimal? ClaRequestedMarketValue1 { get; set; }

        [Column("CLA_Category")]
        public string? ClaRequestedCategory1 { get; set; }

        [Column("CLA_Extent")]
        public decimal? ClaRequestedExtent1 { get; set; }

        // Owner
        [Column("Owner_Name")]
        public string? OwnerName { get; set; }

        [Column("Owner_Email")]
        public string? OwnerEmail { get; set; }

        [Column("Owner_Address1")]
        public string? OwnerAddress1 { get; set; }

        [Column("Owner_Address2")]
        public string? OwnerAddress2 { get; set; }

        [Column("Owner_Address3")]
        public string? OwnerAddress3 { get; set; }

        [Column("Owner_Address4")]
        public string? OwnerAddress4 { get; set; }

        [Column("Owner_Address5")]
        public string? OwnerAddress5 { get; set; }

        // Third party
        [Column("ThirdParty_Name")]
        public string? ThirdPartyName { get; set; }

        [Column("ThirdParty_Email")]
        public string? ThirdPartyEmail { get; set; }

        // Admin
        [Column("Admin_Name")]
        public string? AdminName { get; set; }

        [Column("Admin_Email")]
        public string? AdminEmail { get; set; }

        // Dates
        [Column("Letter_Date")]
        public DateTime? LetterDate { get; set; }

        [Column("Representation_Close_Date")]
        public DateTime? RepresentationCloseDate { get; set; }

        [Column("Notification_Deadline")]
        public DateTime? NotificationDeadline { get; set; }

        // Appeal pack
        [Column("AppealPackPath")]
        public string? AppealPackPath { get; set; }

        [Column("AppealPackFileName")]
        public string? AppealPackFileName { get; set; }

        [Column("AppealPackZipPath")]
        public string? AppealPackZipPath { get; set; }

        [Column("AppealPackExists")]
        public bool AppealPackExists { get; set; }

        [Column("AppealPackUploadedBy")]
        public string? AppealPackUploadedBy { get; set; }

        [Column("AppealPackUploadedAtUtc")]
        public DateTime? AppealPackUploadedAtUtc { get; set; }

        // PDF
        [Column("PdfPath")]
        public string? PdfPath { get; set; }

        [Column("PdfExists")]
        public bool PdfExists { get; set; }

        [Column("PrintedBy")]
        public string? PrintedBy { get; set; }

        [Column("PrintedAtUtc")]
        public DateTime? PrintedAtUtc { get; set; }

        // Email
        [Column("EmailTo")]
        public string? EmailTo { get; set; }

        [Column("EmailCc")]
        public string? EmailCc { get; set; }

        [Column("EmailSubject")]
        public string? EmailSubject { get; set; }

        [Column("EmailBody")]
        public string? EmailBody { get; set; }

        [Column("EmlPath")]
        public string? EmlPath { get; set; }

        [Column("SentAtUtc")]
        public DateTime? SentAtUtc { get; set; }

        [Column("SentBy")]
        public string? SentBy { get; set; }

        [Column("EmailAttemptCount")]
        public int EmailAttemptCount { get; set; }

        [Column("LastEmailAttemptAtUtc")]
        public DateTime? LastEmailAttemptAtUtc { get; set; }

        [Column("EmailError")]
        public string? EmailError { get; set; }

        [Column("Status")]
        public string Status { get; set; } = "Imported";

        // Import/audit
        [Column("SourceFileName")]
        public string? SourceFileName { get; set; }

        [Column("SourceSheetName")]
        public string? SourceSheetName { get; set; }

        [Column("SourceRowNumber")]
        public int? SourceRowNumber { get; set; }

        [Column("IsActive")]
        public bool IsActive { get; set; }

        [Column("CreatedBy")]
        public string? CreatedBy { get; set; }

        [Column("CreatedAtUtc")]
        public DateTime CreatedAtUtc { get; set; }

        [Column("UpdatedBy")]
        public string? UpdatedBy { get; set; }

        [Column("UpdatedAtUtc")]
        public DateTime? UpdatedAtUtc { get; set; }

        /*
         * These properties are referenced by older letter code,
         * but the current SQL table does not contain matching columns.
         * Prevent EF Core from including them in generated SQL.
         */

        [NotMapped]
        public decimal? RollMarketValue2 { get; set; }

        [NotMapped]
        public decimal? RollMarketValue3 { get; set; }

        [NotMapped]
        public string? RollCategory2 { get; set; }

        [NotMapped]
        public string? RollCategory3 { get; set; }

        [NotMapped]
        public decimal? RollExtent2 { get; set; }

        [NotMapped]
        public decimal? RollExtent3 { get; set; }

        [NotMapped]
        public decimal? ObjectionOutcomeMarketValue2 { get; set; }

        [NotMapped]
        public decimal? ObjectionOutcomeMarketValue3 { get; set; }

        [NotMapped]
        public string? ObjectionOutcomeCategory2 { get; set; }

        [NotMapped]
        public string? ObjectionOutcomeCategory3 { get; set; }

        [NotMapped]
        public decimal? ObjectionOutcomeExtent2 { get; set; }

        [NotMapped]
        public decimal? ObjectionOutcomeExtent3 { get; set; }

        [NotMapped]
        public decimal? ClaRequestedMarketValue2 { get; set; }

        [NotMapped]
        public decimal? ClaRequestedMarketValue3 { get; set; }

        [NotMapped]
        public string? ClaRequestedCategory2 { get; set; }

        [NotMapped]
        public string? ClaRequestedCategory3 { get; set; }

        [NotMapped]
        public decimal? ClaRequestedExtent2 { get; set; }

        [NotMapped]
        public decimal? ClaRequestedExtent3 { get; set; }

        [NotMapped]
        public string? ThirdPartyAddress1 { get; set; }

        [NotMapped]
        public string? ThirdPartyAddress2 { get; set; }

        [NotMapped]
        public string? ThirdPartyAddress3 { get; set; }

        [NotMapped]
        public string? ThirdPartyAddress4 { get; set; }

        [NotMapped]
        public string? ThirdPartyAddress5 { get; set; }

        [NotMapped]
        public bool IsMultipurpose { get; set; }
    }
}
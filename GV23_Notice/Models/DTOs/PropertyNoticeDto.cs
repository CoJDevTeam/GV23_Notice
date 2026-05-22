namespace GV23_Notice.Models.DTOs
{
    public class PropertyNoticeDto
    {
        // =============================
        // Core Property Identifiers
        // =============================
        public string? PremiseId { get; set; }
        public string? PropertyId { get; set; }
        public string? UnitKey { get; set; }
        public string? ValuationKey { get; set; }

        // =============================
        // Property Description
        // =============================
        public string? PropertyDesc { get; set; }
        public string? TownNameDesc { get; set; }
        public string? LisStreetAddress { get; set; }

        // =============================
        // Land / Scheme Details
        // =============================
        public string? Erf { get; set; }
        public string? Ptn { get; set; }
        public string? SchemeName { get; set; }
        public string? SchemeNumber { get; set; }
        public string? SchemeYear { get; set; }
        public string? UnitNo { get; set; }

        // =============================
        // Valuation Information
        // =============================
        public string? MarketValue { get; set; }
        public string? OldMarketValue { get; set; }   // Section 53
        public string? NewMarketValue { get; set; }   // Section 53
        public string? RateableArea { get; set; }
        public string? CatDesc { get; set; }

        public string? ValuationDate { get; set; }
        public string? WefDate { get; set; }
        public string? Sector { get; set; }

        // =============================
        // Ownership & Contact
        // =============================
        public string? OwnerName { get; set; }
        public string? EmailAddress { get; set; }   // EMAIL_ADDR alias


        public string? Email { get; set; }
        public string? ADDR1 { get; set; }
        public string? ADDR2 { get; set; }
        public string? ADDR3 { get; set; }
        public string? ADDR4 { get; set; }
        public string? ADDR5 { get; set; }


        // =============================
        // Objection / Appeal (51–53)
        // =============================
        public string? ObjectionNumber { get; set; }
        public string? AppealNumber { get; set; }

        /// <summary>
        /// "Prop Owner" or "Third_Party" — populated for Section 52.
        /// Drives the _Prop_Owner suffix on PDF and .eml filenames.
        /// </summary>
        public string? AppealType { get; set; }

        public string? ObjectionDate { get; set; }
        public string? AppealDate { get; set; }

        public string? OutcomeDescription { get; set; }
        public string? ReviewComments { get; set; }

        // =============================
        // Section 51 (Third Party)
        // =============================
        public string? ObjectorName { get; set; }

        // =============================
        // Section 49 Specific
        // =============================
        public string? Reason { get; set; }
        public string? Re { get; set; }

        // =============================
        // Runtime / UI Helpers
        // =============================
        public string? NoticeType { get; set; }
        public bool HasEmail => !string.IsNullOrWhiteSpace(EmailAddress);



    }
}

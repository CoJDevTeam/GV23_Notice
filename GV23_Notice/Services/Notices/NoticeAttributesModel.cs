namespace GV23_Notice.Services.Notices
{
    /// <summary>
    /// Common property + owner attributes used by valuation notices
    /// (Section 49 now, expandable for others if needed)
    /// </summary>
    public sealed class NoticeAttributesModel
    {
        // =========================
        // ADDRESS BLOCK (POSTAL)
        // =========================
        public string? ADDR1 { get; set; }
        public string? ADDR2 { get; set; }
        public string? ADDR3 { get; set; }
        public string? ADDR4 { get; set; }
        public string? ADDR5 { get; set; }

        // =========================
        // PROPERTY IDENTIFIERS
        // =========================
        public string? ObjectionNo { get; set; }        // e.g. OBJ-GV23-1
        public string? PremiseId { get; set; }          // LIS / Premise ID
        public string? PropertyDesc { get; set; }       // Portion / Erf description
        public string? LisStreetAddress { get; set; }   // Physical address

        // =========================
        // VALUATION DETAILS
        // =========================
        public string? MarketValue { get; set; }        // formatted or raw (builder formats)
        public string? RateableArea { get; set; }       // extent / m²
        public string? CatDesc { get; set; }            // property category
        public string? Reason { get; set; }             // remarks / notes

        // =========================
        // OWNER (OPTIONAL – FUTURE)
        // =========================
        public string? OwnerName { get; set; }
        public string? OwnerEmail { get; set; }

        // =========================
        // SAFETY / NORMALISATION
        // =========================
        public void Normalise()
        {
            ADDR1 ??= string.Empty;
            ADDR2 ??= string.Empty;
            ADDR3 ??= string.Empty;
            ADDR4 ??= string.Empty;
            ADDR5 ??= string.Empty;

            PropertyDesc ??= string.Empty;
            LisStreetAddress ??= string.Empty;
            MarketValue ??= string.Empty;
            RateableArea ??= string.Empty;
            CatDesc ??= string.Empty;
            Reason ??= string.Empty;
        }
    }
}


namespace GV23_Notice.Models.DTOs
{
    public sealed class S49PreviewDbData
    {
        public int RollId { get; set; }

        // roll row
        public string PremiseId { get; set; } = "";
        public string? PropertyDesc { get; set; }
        public string? LisStreetAddress { get; set; }
        public string? ValuationKey { get; set; }   // VALUATIONKEY
        public string? CatDesc { get; set; }
        public decimal? RateableArea { get; set; }
        public decimal? MarketValue { get; set; }
        public string? Reason { get; set; }
        public string? ValuationSplitIndicator { get; set; }

        // contact
        public string? Email { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }
        public string? PremiseAddress { get; set; }
        public string? AccountNo { get; set; }
    }

    public sealed class S51PreviewDbData
    {
        public int RollId { get; set; }
        public string ObjectionNo { get; set; } = "";
        public string? ObjectorType { get; set; }

        // Obj_Property_Info (sample fields typically used)
        public string? PremiseId { get; set; }
        public string? PropertyDesc { get; set; }
        public string? Email { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        // Obj_Section6 (Old/New + multipurpose split fields)
        public string? OldCategory { get; set; }
        public string? Old2Category { get; set; }
        public string? Old3Category { get; set; }

        public decimal? OldExtent { get; set; }
        public decimal? Old2Extent { get; set; }
        public decimal? Old3Extent { get; set; }

        public decimal? OldMarketValue { get; set; }
        public decimal? Old2MarketValue { get; set; }
        public decimal? Old3MarketValue { get; set; }

        public string? NewCategory { get; set; }
        public string? New2Category { get; set; }
        public string? New3Category { get; set; }

        public decimal? NewExtent { get; set; }
        public decimal? New2Extent { get; set; }
        public decimal? New3Extent { get; set; }

        public decimal? NewMarketValue { get; set; }
        public decimal? New2MarketValue { get; set; }
        public decimal? New3MarketValue { get; set; }

        public string? ObjectionReasons { get; set; }
    }

    public sealed class S52PreviewDbData
    {
        public int RollId { get; set; }
        public string AppealNo { get; set; } = "";
        public string? ObjectionNo { get; set; }

        public string? AUserId { get; set; } // System_Generated vs user
        public string? PremiseId { get; set; }
        public string? ValuationKey { get; set; }
        public string? PropertyDesc { get; set; }
        public string? Email { get; set; }

        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        public string? Town { get; set; }
        public string? Erf { get; set; }
        public string? Ptn { get; set; }
        public string? Re { get; set; }

        public decimal? AppMarketValue { get; set; }
        public decimal? AppMarketValue2 { get; set; }
        public decimal? AppMarketValue3 { get; set; }

        public decimal? AppExtent { get; set; }
        public decimal? AppExtent2 { get; set; }
        public decimal? AppExtent3 { get; set; }

        public string? AppCategory { get; set; }
        public string? AppCategory2 { get; set; }
        public string? AppCategory3 { get; set; }
    }

    public sealed class S53PreviewDbData
    {
        public int RollId { get; set; }
        public string ObjectionNo { get; set; } = "";

        public string? PremiseId { get; set; }
        public string? ValuationKey { get; set; }
        public string? PropertyDesc { get; set; }
        public string? Email { get; set; }

        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        // GV (roll values)
        public decimal? GvMarketValue { get; set; }
        public decimal? GvMarketValue2 { get; set; }
        public decimal? GvMarketValue3 { get; set; }

        public decimal? GvExtent { get; set; }
        public decimal? GvExtent2 { get; set; }
        public decimal? GvExtent3 { get; set; }

        public string? GvCategory { get; set; }
        public string? GvCategory2 { get; set; }
        public string? GvCategory3 { get; set; }

        // MVD decision values
        public decimal? MvdMarketValue { get; set; }
        public decimal? MvdMarketValue2 { get; set; }
        public decimal? MvdMarketValue3 { get; set; }

        public decimal? MvdExtent { get; set; }
        public decimal? MvdExtent2 { get; set; }
        public decimal? MvdExtent3 { get; set; }

        public string? MvdCategory { get; set; }
        public string? MvdCategory2 { get; set; }
        public string? MvdCategory3 { get; set; }

        public string? Section52Review { get; set; }
        public DateTime? AppealCloseDate { get; set; }
        public DateTime? BatchDate { get; set; }
        public string? BatchName { get; set; }
    }

    public sealed class DJPreviewDbData
    {
        public int RollId { get; set; }
        public string ObjectionNo { get; set; } = "";
        public string? PremiseId { get; set; }
        public string? PropertyDesc { get; set; }

        public string? Email { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }
    }

    public sealed class InvalidPreviewDbData
    {
        public int RollId { get; set; }
        public string ObjectionNo { get; set; } = "";
        public string? PremiseId { get; set; }
        public string? PropertyDesc { get; set; }

        public string? Email { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        public string? ObjectionStatus { get; set; } // Invalid-Objection / Invalid-Omission
    }
}

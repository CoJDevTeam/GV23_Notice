namespace GV23_Notice.Services.Notices.Section53
{
    namespace COJ_Notice_2026.Models.ViewModels.Section53
    {
        public sealed class Section53MvdRow
        {
            // Identity / refs
            public string? ObjectionNo { get; set; }
            public string? ValuationKey { get; set; }

            // Recipient / address
            public string? Email { get; set; }
            public string? ObjectorName { get; set; } // optional, not used by PDF yet
            public string? Addr1 { get; set; }
            public string? Addr2 { get; set; }
            public string? Addr3 { get; set; }
            public string? Addr4 { get; set; }
            public string? Addr5 { get; set; }

            // Property
            public string? PropertyDesc { get; set; }

            // Dates
            public DateTime? AppealCloseDate { get; set; }

            // Section 52 review (text)
            public string? Section52Review { get; set; }  // e.g. "Yes" / "No" / "N/A" / long text

            // Multi split flag (your PDF currently uses split columns on one record)
            public bool IsMulti { get; set; }
            public string? RollName { get; set; }
            // ======================
            // Entry in Valuation Roll (GV)
            // ======================
            public string? Gv_Market_Value { get; set; }
            public string? Gv_Extent { get; set; }
            public string? Gv_Category { get; set; }

            public string? Gv_Market_Value2 { get; set; }
            public string? Gv_Extent2 { get; set; }
            public string? Gv_Category2 { get; set; }

            public string? Gv_Market_Value3 { get; set; }
            public string? Gv_Extent3 { get; set; }
            public string? Gv_Category3 { get; set; }

            // ======================
            // Municipal Valuer’s Decision (MVD)
            // ======================
            public string? Mvd_Market_Value { get; set; }
            public string? Mvd_Extent { get; set; }
            public string? Mvd_Category { get; set; }

            public string? Mvd_Market_Value2 { get; set; }
            public string? Mvd_Extent2 { get; set; }
            public string? Mvd_Category2 { get; set; }

            public string? Mvd_Market_Value3 { get; set; }
            public string? Mvd_Extent3 { get; set; }
            public string? Mvd_Category3 { get; set; }

            public string? rollName { get; set; }
        }
    }
}

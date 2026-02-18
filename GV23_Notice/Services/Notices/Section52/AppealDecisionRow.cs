namespace GV23_Notice.Services.Notices.Section52
{
    public sealed class AppealDecisionRow
    {
        public string? A_UserID { get; set; } // "System_Generated" => Review

        public string? ADDR1 { get; set; }
        public string? ADDR2 { get; set; }
        public string? ADDR3 { get; set; }
        public string? ADDR4 { get; set; }
        public string? ADDR5 { get; set; }

        public string? Email { get; set; }

        public string? Property_desc { get; set; }

        public string? Town { get; set; }
        public string? ERF { get; set; }
        public string? PTN { get; set; }
        public string? RE { get; set; }

        public string? Objection_No { get; set; }
        public string? Appeal_No { get; set; } // only shown if !isReview

        public string? App_Category { get; set; }

        public object? App_Extent { get; set; }
        public object? App_Extent2 { get; set; }
        public object? App_Extent3 { get; set; }

        public object? App_Market_Value { get; set; }
        public object? App_Market_Value2 { get; set; }
        public object? App_Market_Value3 { get; set; }

        public string? valuation_Key { get; set; }
    }
}

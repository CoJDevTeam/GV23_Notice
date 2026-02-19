namespace GV23_Notice.Services.Notices.Section51
{
    public sealed class Section51NoticeContext
    {
        // ================= PDF Layout =================
        public string HeaderImagePath { get; set; } = "";

        // ================= Dates =================
        public DateTime LetterDate { get; set; }
        public DateTime SubmissionsCloseDate { get; set; }

        // ================= Portal =================
        public string PortalUrl { get; set; } = "https://objections.joburg.org.za";

        // ================= Sign-off =================
        public string? SignOffName { get; set; }
        public string? SignOffTitle { get; set; }

       
     

        // Optional contact line
        public string EnquiriesLine { get; set; } =
            "For any enquiries, please contact us on 011 407 6622 or 011 407 6597/valuationenquiries@joburg.org.za";

    }


    public sealed class Section6Row
    {
        // ================= OLD (GV Roll) =================
        public string? Old_Market_Value { get; set; }
        public string? Old_Category { get; set; }
        public string? Old_Extent { get; set; }

        public string? Old2_Market_Value { get; set; }
        public string? Old2_Category { get; set; }
        public string? Old2_Extent { get; set; }

        public string? Old3_Market_Value { get; set; }
        public string? Old3_Category { get; set; }
        public string? Old3_Extent { get; set; }

        // ================= OBJECTOR REQUEST =================
        public string? New_Market_Value { get; set; }
        public string? New_Category { get; set; }
        public string? New_Extent { get; set; }

        public string? New2_Market_Value { get; set; }
        public string? New2_Category { get; set; }
        public string? New2_Extent { get; set; }

        public string? New3_Market_Value { get; set; }
        public string? New3_Category { get; set; }
        public string? New3_Extent { get; set; }



    }
}

namespace GV23_Notice.Services.Notices.DearJohnny
{
    public sealed class DearJonnyPdfContext
    {
        public string HeaderImagePath { get; set; } = "";
        public DateTime LetterDate { get; set; } = DateTime.Today;

        public string EnquiriesLine { get; set; } =
            "For any enquiries, please contact us on 011 407 6622 or 011 407 6597, or email us at valuationenquiries@joburg.org.za";
    }

    public sealed class DearJonnyPdfData
    {
        public string ObjectionNo { get; set; } = "";
        public string PropertyDescription { get; set; } = "";

        public string RollName { get; set; } = "";   // ✅ (rename from rollName for C# consistency)

        public string Addr1 { get; set; } = "";
        public string Addr2 { get; set; } = "";
        public string Addr3 { get; set; } = "";
        public string Addr4 { get; set; } = "";
        public string Addr5 { get; set; } = "";

        // ✅ NEW
        public string ValuationKey { get; set; } = "";

        public string? RecipientName { get; set; }
    }
}

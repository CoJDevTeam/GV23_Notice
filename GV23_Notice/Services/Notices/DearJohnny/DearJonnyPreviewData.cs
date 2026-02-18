namespace GV23_Notice.Services.Notices.DearJohnny
{
    public sealed class DearJonnyPreviewData
    {
        // Common preview pieces
        public string HeaderImagePath { get; set; } = "";
        public DateTime LetterDate { get; set; } = DateTime.Today;

        public string EnquiriesLine { get; set; } =
            "For any enquiries, please contact us on 011 407 6622 or 011 407 6597, or email us at valuationenquiries@joburg.org.za";

        // Dummy roll + refs
        public string RollName { get; set; } = "GV23";
        public string ObjectionNo { get; set; } = "OBJ-GV23-1";
        public string PropertyDescription { get; set; } = "PORTION 2 ERF 201 ROSEBANK";

        // Dummy address block
        public string Addr1 { get; set; } = "Notice Sample";
        public string Addr2 { get; set; } = "66 Jorissen Place";
        public string Addr3 { get; set; } = "Jorissen Street";
        public string Addr4 { get; set; } = "Braamfontein";
        public string Addr5 { get; set; } = "2001";
    }
}

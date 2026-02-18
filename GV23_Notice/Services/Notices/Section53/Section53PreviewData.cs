namespace GV23_Notice.Services.Notices.Section53
{
    public sealed class Section53PreviewData
    {
        public string ObjectionNo { get; set; } = "";
        public string PropertyDesc { get; set; } = "SAMPLE PROPERTY";
        public string Email { get; set; } = "NoticeSample@joburg.org.za";

        public string Addr1 { get; set; } = "66 Jorissen Place";
        public string Addr2 { get; set; } = "66 Jorissen Street";
        public string Addr3 { get; set; } = "Braamfontein";
        public string Addr4 { get; set; } = "Johannesburg";
        public string Addr5 { get; set; } = "2001";

        public string ValuationKey { get; set; } = "VALKEY-SAMPLE";
        public DateTime AppealCloseDate { get; set; } = DateTime.Today.AddDays(45);
        public string Section52Review { get; set; } = "N/A";

        // GV / MVD split columns (1–3)
        public string GvMarketValue { get; set; } = "R 3 000 000";
        public string GvExtent { get; set; } = "500";
        public string GvCategory { get; set; } = "Residential";

        public string MvdMarketValue { get; set; } = "R 3 200 000";
        public string MvdExtent { get; set; } = "500";
        public string MvdCategory { get; set; } = "Residential";
    }
}

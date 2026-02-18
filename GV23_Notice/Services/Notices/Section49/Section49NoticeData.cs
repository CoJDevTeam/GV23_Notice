namespace GV23_Notice.Services.Notices.Section49
{
    public sealed class Section49NoticeData
    {
        public string RecipientName { get; set; } = "";
        public string AddressLine { get; set; } = "";
        public string ObjectionNo { get; set; } = "";

        public DateTime ObjectionStartDate { get; set; }
        public DateTime ObjectionEndDate { get; set; }
        public DateTime? ExtensionDate { get; set; }
    }
}

namespace GV23_Notice.Domain.Email
{
    public sealed class EmailTemplateOptions
    {
        public string FromAddress { get; set; } = "";
        public string FromName { get; set; } = "";

        public EnquiriesOptions Enquiries { get; set; } = new();
        public SignOffOptions SignOff { get; set; } = new();

        public string PortalUrl { get; set; } = "https://objections.joburg.org.za/";

        public string? BaseUrl { get; set; }

        public string[] ApprovalRecipients { get; set; } = Array.Empty<string>();
        public string[] CorrectionRecipients { get; set; } = Array.Empty<string>();

        public sealed class EnquiriesOptions
        {
            public string Tel1 { get; set; } = "011 407-6622";
            public string Tel2 { get; set; } = "011 407-6597";
            public string Email { get; set; } = "valuationenquiries@joburg.org.za";
        }

        public sealed class SignOffOptions
        {
            public string Line1 { get; set; } = "Kind Regards,";
            public string Line2 { get; set; } = "City of Johannesburg";
            public string Line3 { get; set; } = "Valuation Services Department";


        }
    }
}

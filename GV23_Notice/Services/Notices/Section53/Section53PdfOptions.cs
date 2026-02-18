namespace GV23_Notice.Services.Notices.Section53
{
    public sealed class Section53PdfOptions
    {
        // Relative to wwwroot, e.g. "Images/Obj_Header.PNG"
        public string? HeaderImageRelativePath { get; set; } = "Images/Obj_Header.PNG";

        public string? PortalUrl { get; set; } = "https://objections.joburg.org.za/";
        public string? ContactLine { get; set; } =
            "For any enquiries, please contact us on 011 407 6622 or 011 407 6597, or email valuationenquiries@joburg.org.za";

        public string? MunicipalValuerName { get; set; } = "S. Faiaz";
        public string? MunicipalValuerTitle { get; set; } = "Municipal Valuer";
    }
}

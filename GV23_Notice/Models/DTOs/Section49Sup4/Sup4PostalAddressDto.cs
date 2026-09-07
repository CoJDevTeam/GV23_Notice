namespace GV23_Notice.Models.DTOs.Section49Sup4
{
    public sealed class Sup4PostalAddressDto
    {
        public int Id { get; set; }

        public string PremiseId { get; set; } = string.Empty;

        public string? EmailAddr { get; set; }

        public string? TelNo { get; set; }

        public string? Addr1 { get; set; }

        public string? Addr2 { get; set; }

        public string? Addr3 { get; set; }

        public string? Addr4 { get; set; }

        public string? Addr5 { get; set; }

        public string? PremiseAddress { get; set; }

        public string? PremiseTown { get; set; }

        public DateTime? WefDate { get; set; }

        public bool HasEmail =>
            !string.IsNullOrWhiteSpace(EmailAddr);
    }
}
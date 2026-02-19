namespace GV23_Notice.Services.Notices.Section51
{
    public sealed class Section51NoticeData
    {
        // ================= Recipient =================
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        public string? OwnerName { get; set; }
        public string? Email { get; set; }

        // ================= Roll / Objection =================
        public string ObjectionNo { get; set; } = "";
        public string? RollName { get; set; }

        // ================= Property =================
        public string PropertyDesc { get; set; } = "";

        // ================= Section 51 =================
        public string? Section51Pin { get; set; }

        // ================= Comparison =================
        public Section6Row? Section6 { get; set; }

        public string? PropertyFrom { get; set; } // "Omission" or "Printed"

     
            // recipient/address
                   // “MELROSE NORTH Erf ...”
            public DateOnly? EffectiveDate { get; set; }           // “With Effect Date 01 July 2023”

            // ✅ Trace key (required by you)
            public string? ValuationKey { get; set; }

           
        
    }
}

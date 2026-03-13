namespace GV23_Notice.Models.DTOs
{
    public sealed class DjRow
    {
        public string? ObjectionNo { get; set; }
        public string? PropertyDesc { get; set; }
        public string? ValuationKey { get; set; }

        // Postal address — split into 5 lines matching the address block on the PDF.
        // Populated from Obj_Section1 (Objector_Postal_1-4 → Owner_Address_1-4 fallback).
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        public string? Email { get; set; }
    }

    /// <summary>
    /// Row fetched from InvalidNoticeTable (in the roll SourceDb) for a single
    /// ObjectionNo + BatchName. Used by NoticeBatchPrintService and NoticeBatchEmailService.
    /// </summary>
    public sealed class InRow
    {
        public string? ObjectionNo { get; set; }
        public string? PropertyDesc { get; set; }
        public string? ValuationKey { get; set; }

        /// <summary>'InvalidObjection' or 'InvalidOmission' — maps to InvalidNoticeKind enum.</summary>
        public string? Kind { get; set; }

        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        public string? Email { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GV23_Notice.Domain.Section49Sup4
{
    [Table("Section49Table", Schema = "dbo")]
    public sealed class Section49Sup4Record
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [Column("PREMISE_ID")]
        [MaxLength(50)]
        public string PremiseId { get; set; } = string.Empty;

        [Required]
        [Column("Property_Desc")]
        [MaxLength(500)]
        public string PropertyDesc { get; set; } = string.Empty;

        [Column("ADDR1")]
        [MaxLength(500)]
        public string? Addr1 { get; set; }

        [Column("ADDR2")]
        [MaxLength(500)]
        public string? Addr2 { get; set; }

        [Column("ADDR3")]
        [MaxLength(500)]
        public string? Addr3 { get; set; }

        [Column("ADDR4")]
        [MaxLength(500)]
        public string? Addr4 { get; set; }

        [Column("ADDR5")]
        [MaxLength(500)]
        public string? Addr5 { get; set; }

        [Column("EMAIL_ADDR")]
        [MaxLength(320)]
        public string? EmailAddr { get; set; }

        /// <summary>
        /// Yes = source postal record had an email address.
        /// No  = source postal record did not have an email address.
        /// </summary>
        [Required]
        [Column("Has_Email")]
        [MaxLength(3)]
        public string HasEmail { get; set; } = "No";

        [Required]
        [Column("Batch_Name")]
        [MaxLength(100)]
        public string BatchName { get; set; } = string.Empty;

        [Column("Batch_Date")]
        public DateTime BatchDate { get; set; }

        [Column("Created_Date")]
        public DateTime CreatedDate { get; set; }

        [Column("Created_By")]
        [MaxLength(150)]
        public string? CreatedBy { get; set; }

        [Column("Pdf_Path")]
        [MaxLength(2000)]
        public string? PdfPath { get; set; }

        [Column("Eml_Path")]
        [MaxLength(2000)]
        public string? EmlPath { get; set; }

        [Column("Sent_Date")]
        public DateTime? SentDate { get; set; }

        [Column("Sent_By")]
        [MaxLength(150)]
        public string? SentBy { get; set; }

        /// <summary>
        /// The client's real email address from Supp4_Postal_address.
        /// This must never be overwritten by test mode.
        /// </summary>
        [Column("Original_Email_Addr")]
        [MaxLength(320)]
        public string? OriginalEmailAddr { get; set; }

        /// <summary>
        /// Actual SMTP recipient.
        /// Production = OriginalEmailAddr
        /// Test mode  = configured test recipient.
        /// </summary>
        [Column("Actual_Sent_To")]
        [MaxLength(320)]
        public string? ActualSentTo { get; set; }

        [Column("Is_Test_Mode")]
        public bool IsTestMode { get; set; }

        [Required]
        [Column("Send_Status")]
        [MaxLength(50)]
        public string SendStatus { get; set; } = Section49Sup4Statuses.Pending;

        [Column("Error_Message")]
        [MaxLength(2000)]
        public string? ErrorMessage { get; set; }
    }
}
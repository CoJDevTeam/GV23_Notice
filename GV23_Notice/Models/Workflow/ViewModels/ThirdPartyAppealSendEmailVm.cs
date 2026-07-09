namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class ThirdPartyAppealSendEmailVm
    {
        public int? NoticeSettingsId { get; set; }
        public int RollId { get; set; }

        public string RollShortCode { get; set; } = "";
        public string ValuationPeriod { get; set; } = "";
        public string Notice { get; set; } = "Third-Party Appeal Application";

        public int TotalPrinted { get; set; }
        public int TotalReadyToSend { get; set; }
        public int TotalSent { get; set; }
        public int TotalFailed { get; set; }
        public int TotalNoOwnerEmail { get; set; }

        public string EmailSubjectTemplate { get; set; } =
            "NOTICE OF THIRD PARTY APPEAL APPLICATION FOR THE {ValuationPeriod} – {PropertyDescription}";

        public string EmailBodyTemplate { get; set; } =
@"Dear Client,

Property Description: {PropertyDescription}

Please be advised that the City has received an appeal application relating to the above-mentioned property from a party who is not the registered owner. As this matter may affect your property interests, your attention and participation are required.

Attached herewith is the Valuation Appeal Board formal notice {AppealNo} for your attention, review, and necessary action.

For any further enquiries or assistance, please contact us via email at {AdminEmail} and valuationenquiries@joburg.org.za.

Your urgent attention is required.

Yours faithfully,
Valuation Appeal Board Secretariat
City of Johannesburg";

        public List<ThirdPartyAppealSendEmailItemVm> Items { get; set; } = new();
    }

    public class ThirdPartyAppealSendEmailItemVm
    {
        public int Id { get; set; }
        public string? PremiseId { get; set; }
        public string AppealNo { get; set; } = "";
        public string ObjectionNo { get; set; } = "";
        public string PropertyDescription { get; set; } = "";

        public string OwnerName { get; set; } = "";
        public string OwnerEmail { get; set; } = "";

        public string ThirdPartyName { get; set; } = "";
        public string ThirdPartyEmail { get; set; } = "";

        public string AdminName { get; set; } = "";
        public string AdminEmail { get; set; } = "";

        public string EmailTo { get; set; } = "";
        public string EmailCc { get; set; } = "";

        public string PdfPath { get; set; } = "";
        public string AppealPackZipPath { get; set; } = "";
        public string EmlPath { get; set; } = "";

        public string Status { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }
}

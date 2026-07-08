namespace GV23_Notice.Models.Workflow.ViewModels
{
    public class ThirdPartyAppealPreviewItemVm
    {
        public int Id { get; set; }

        public string AppealNo { get; set; } = "";
        public string ObjectionNo { get; set; } = "";
        public string PremiseId { get; set; } = "";
        public string ValuationKey { get; set; } = "";

        public string PropertyType { get; set; } = "";
        public string PropertyDescription { get; set; } = "";

        public string OwnerName { get; set; } = "";
        public string OwnerEmail { get; set; } = "";
        public string OwnerAddressText { get; set; } = "";

        public string ThirdPartyName { get; set; } = "";
        public string ThirdPartyEmail { get; set; } = "";
        public string ThirdPartyAddressText { get; set; } = "";

        public string AdminName { get; set; } = "";
        public string AdminEmail { get; set; } = "";

        public DateTime? DateAdded { get; set; }
        public DateTime? ScheduleDate { get; set; }
        public DateTime? HearingDate { get; set; }

        public string RollMarketValue1 { get; set; } = "";
        public string RollMarketValue2 { get; set; } = "";
        public string RollMarketValue3 { get; set; } = "";

        public string RollCategory1 { get; set; } = "";
        public string RollCategory2 { get; set; } = "";
        public string RollCategory3 { get; set; } = "";

        public string RollExtent1 { get; set; } = "";
        public string RollExtent2 { get; set; } = "";
        public string RollExtent3 { get; set; } = "";

        public string ObjectionOutcomeMarketValue1 { get; set; } = "";
        public string ObjectionOutcomeMarketValue2 { get; set; } = "";
        public string ObjectionOutcomeMarketValue3 { get; set; } = "";

        public string ObjectionOutcomeCategory1 { get; set; } = "";
        public string ObjectionOutcomeCategory2 { get; set; } = "";
        public string ObjectionOutcomeCategory3 { get; set; } = "";

        public string ObjectionOutcomeExtent1 { get; set; } = "";
        public string ObjectionOutcomeExtent2 { get; set; } = "";
        public string ObjectionOutcomeExtent3 { get; set; } = "";

        public string AppellantRequestMarketValue1 { get; set; } = "";
        public string AppellantRequestMarketValue2 { get; set; } = "";
        public string AppellantRequestMarketValue3 { get; set; } = "";

        public string AppellantRequestCategory1 { get; set; } = "";
        public string AppellantRequestCategory2 { get; set; } = "";
        public string AppellantRequestCategory3 { get; set; } = "";

        public string AppellantRequestExtent1 { get; set; } = "";
        public string AppellantRequestExtent2 { get; set; } = "";
        public string AppellantRequestExtent3 { get; set; } = "";

        public string AppReason { get; set; } = "";

        public bool IsMultipurpose { get; set; }

        public string PdfPath { get; set; } = "";
        public string AppealPackZipPath { get; set; } = "";
        public string Status { get; set; } = "";
    }
}

using GV23_Notice.Models.Workflow.ViewModels;

namespace GV23_Notice.Models.Corrections.ViewModels
{
    public class CorrectionPreviewItemVm
    {
        public string? ObjectionNo { get; set; }
        public string? AppealNo { get; set; }
        public string? QueryNo { get; set; }
        public string? ReviewNo { get; set; }

        public string? PremiseId { get; set; }
        public string? UnitKey { get; set; }
        public string? ValuationKey { get; set; }

        public string? PropertyDesc { get; set; }
        public string? PropertyType { get; set; }

        public string? RecipientRole { get; set; }
        public string? RecipientName { get; set; }
        public string? RecipientEmail { get; set; }

        public string? ADDR1 { get; set; }
        public string? ADDR2 { get; set; }
        public string? ADDR3 { get; set; }
        public string? ADDR4 { get; set; }
        public string? ADDR5 { get; set; }

        public string? OldCategory { get; set; }
        public string? OldCategory2 { get; set; }
        public string? OldCategory3 { get; set; }

        public string? OldMarketValue { get; set; }
        public string? OldMarketValue2 { get; set; }
        public string? OldMarketValue3 { get; set; }

        public string? OldExtent { get; set; }
        public string? OldExtent2 { get; set; }
        public string? OldExtent3 { get; set; }

        public string? NewCategory { get; set; }
        public string? NewCategory2 { get; set; }
        public string? NewCategory3 { get; set; }

        public string? NewMarketValue { get; set; }
        public string? NewMarketValue2 { get; set; }
        public string? NewMarketValue3 { get; set; }

        public string? NewExtent { get; set; }
        public string? NewExtent2 { get; set; }
        public string? NewExtent3 { get; set; }

        public string? WEFDate { get; set; }
        public string? WEFDate2 { get; set; }
        public string? WEFDate3 { get; set; }

        public DateTime? BatchDate { get; set; }
        public DateTime? LetterDate { get; set; }
        public DateTime? ClosingDate { get; set; }
        public DateTime? AppealStartDate { get; set; }
        public DateTime? AppealCloseDate { get; set; }

        public string? Section51Pin { get; set; }
        public string? Section52Review { get; set; }

        public string SnapshotJson { get; set; } = "";

        public List<CorrectionFieldRowVm> Fields { get; set; } = new();
    }
}
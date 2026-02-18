using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class SettingsLibraryRowVm
    {
        public int SettingsId { get; set; }
        public int RollId { get; set; }
        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";

        public NoticeKind Notice { get; set; }
        public BatchMode Mode { get; set; }
        public int Version { get; set; }
        public DateTime LetterDate { get; set; }

        public bool IsConfirmed { get; set; }
        public bool IsApproved { get; set; }

        public string? SignaturePath { get; set; }
    }
}

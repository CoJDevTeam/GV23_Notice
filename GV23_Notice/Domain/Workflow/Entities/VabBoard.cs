namespace GV23_Notice.Domain.Workflow.Entities
{
    public sealed class VabBoard
    {
        public int Id { get; set; }

        public string BoardCode { get; set; } = "";
        public string BoardName { get; set; } = "";

        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }

        public bool IsActive { get; set; } = true;

        public string? CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }

        public ICollection<VabBoardMember> Members { get; set; }
            = new List<VabBoardMember>();
    }
}

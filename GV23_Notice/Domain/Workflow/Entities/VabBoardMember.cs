namespace GV23_Notice.Domain.Workflow.Entities
{
    public sealed class VabBoardMember
    {
        public int Id { get; set; }

        public int VabBoardId { get; set; }
        public VabBoard VabBoard { get; set; } = null!;

        public string MemberRole { get; set; } = "";
        public string NameAndSurname { get; set; } = "";

        public string? CojValuerTeam { get; set; }
        public string? CojEmail { get; set; }
        public string? EmailAddress { get; set; }

        public int DisplayOrder { get; set; } = 1;
        public bool IsActive { get; set; } = true;

        public string? CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }
}

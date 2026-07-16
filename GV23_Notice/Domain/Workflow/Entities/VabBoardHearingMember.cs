using GV23_Notice.Domain.Workflow.Entities;

public sealed class VabBoardHearingMember
{
    public int Id { get; set; }

    public int VabBoardId { get; set; }

    public DateTime HearingDate { get; set; }

    public string MemberRole { get; set; } = "";

    public string NameAndSurname { get; set; } = "";

    public string? CojValuerTeam { get; set; }

    public string? CojEmail { get; set; }

    public string? EmailAddress { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public VabBoard? VabBoard { get; set; }
}
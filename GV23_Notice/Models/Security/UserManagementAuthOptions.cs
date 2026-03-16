namespace GV23_Notice.Models.Security
{
    public sealed class UserManagementAuthOptions
    {
        public string SystemId { get; set; } = "";
        public string StoredProcedure { get; set; } = "dbo.Login";
    }
    public sealed class AccessControlOptions
    {
        public Dictionary<string, string> Roles { get; set; } = new();
        public Dictionary<string, string[]> Policies { get; set; } = new();
    }
}

namespace GV23_Notice.Services.Security
{
    public sealed class AppUserProfile
    {
        public int? UserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Role { get; set; } = "";
        public bool HasAccess { get; set; }
    }
    public interface IUserAccessService
    {
        Task<AppUserProfile?> GetUserProfileAsync(string username, CancellationToken ct = default);
    }
}

using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace GV23_Notice.Services.Security
{
    public sealed class UserClaimsTransformation : IClaimsTransformation
    {
        private readonly IUserAccessService _userAccessService;
        private readonly ILogger<UserClaimsTransformation> _logger;

        public UserClaimsTransformation(
            IUserAccessService userAccessService,
            ILogger<UserClaimsTransformation> logger)
        {
            _userAccessService = userAccessService;
            _logger = logger;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity?.IsAuthenticated != true)
                return principal;

            // Prevent duplicate app identity
            if (principal.Identities.Any(i => i.AuthenticationType == "AppUserManagement"))
                return principal;

            var username = principal.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                return principal;

            var profile = await _userAccessService.GetUserProfileAsync(username);

            if (profile == null || !profile.HasAccess)
            {
                _logger.LogWarning("No UserManagement access found for {Username}", username);
                return principal;
            }

            var claims = new List<Claim>
            {
                new Claim("AppClaimsLoaded", "true")
            };

            if (!string.IsNullOrWhiteSpace(profile.FullName))
                claims.Add(new Claim("FullName", profile.FullName.Trim()));

            if (!string.IsNullOrWhiteSpace(profile.Role))
            {
                var role = profile.Role.Trim();
                claims.Add(new Claim(ClaimTypes.Role, role));
                claims.Add(new Claim("Role", role));
            }

            if (profile.UserId.HasValue)
                claims.Add(new Claim("UserId", profile.UserId.Value.ToString()));

            if (!string.IsNullOrWhiteSpace(profile.Username))
                claims.Add(new Claim(ClaimTypes.Name, profile.Username.Trim()));

            // IMPORTANT: create separate identity with explicit role claim type
            var appIdentity = new ClaimsIdentity(
                claims,
                authenticationType: "AppUserManagement",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);

            principal.AddIdentity(appIdentity);

            _logger.LogInformation(
                "App identity added for {Username}. FullName={FullName}, Role={Role}",
                profile.Username,
                profile.FullName,
                profile.Role);

            return principal;
        }
    }
}
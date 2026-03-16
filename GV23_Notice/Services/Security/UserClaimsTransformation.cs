using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace GV23_Notice.Services.Security
{
    public sealed class UserClaimsTransformation : IClaimsTransformation
    {
        private readonly IUserAccessService _userAccessService;

        public UserClaimsTransformation(IUserAccessService userAccessService)
        {
            _userAccessService = userAccessService;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity?.IsAuthenticated != true)
                return principal;

            if (principal.Identity is not ClaimsIdentity identity)
                return principal;

            if (identity.HasClaim("AppClaimsLoaded", "true"))
                return principal;

            var username = principal.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                return principal;

            var user = await _userAccessService.GetUserProfileAsync(username);

            identity.AddClaim(new Claim("AppClaimsLoaded", "true"));

            if (user == null || !user.HasAccess)
                return principal;

            if (!string.IsNullOrWhiteSpace(user.FullName))
                identity.AddClaim(new Claim("FullName", user.FullName));

            if (!string.IsNullOrWhiteSpace(user.Role))
                identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));

            if (user.UserId.HasValue)
                identity.AddClaim(new Claim("UserId", user.UserId.Value.ToString()));

            return principal;
        }
    }
}

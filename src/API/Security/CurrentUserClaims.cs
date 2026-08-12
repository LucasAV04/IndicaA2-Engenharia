using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace API.Security;

public static class CurrentUserClaims
{
    public static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId)
    {
        var subject = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out userId) && userId != Guid.Empty;
    }
}

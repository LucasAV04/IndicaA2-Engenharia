using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;

namespace API.Security;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated is true;

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(value, out var userId) && userId != Guid.Empty
                ? userId
                : null;
        }
    }

    public string? Role => Principal?.FindFirst("role")?.Value;

    public bool IsAdministrator => string.Equals(
        Role,
        AuthorizationRoles.Administrador,
        StringComparison.Ordinal);

    public bool CanAccessUser(Guid userId) =>
        userId != Guid.Empty &&
        (IsAdministrator || UserId is Guid currentUserId && currentUserId == userId);
}

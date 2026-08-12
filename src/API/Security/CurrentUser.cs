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
            return CurrentUserClaims.TryGetUserId(Principal, out var userId)
                ? userId
                : null;
        }
    }

    public string? Role => Principal?.FindFirst("role")?.Value;

    public bool IsAdministrator =>
        IsAuthenticated &&
        UserId.HasValue &&
        string.Equals(Role, AuthorizationRoles.Administrador, StringComparison.Ordinal);

    public bool CanAccessUser(Guid userId)
    {
        if (!IsAuthenticated || userId == Guid.Empty || UserId is not Guid currentUserId)
            return false;

        return IsAdministrator || currentUserId == userId;
    }
}

namespace API.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Role { get; }

    bool IsAdministrator { get; }

    bool CanAccessUser(Guid userId);
}

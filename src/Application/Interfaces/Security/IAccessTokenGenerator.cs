using Domain.Entities;

namespace Application.Interfaces.Security;

public interface IAccessTokenGenerator
{
    AccessTokenResult Generate(Usuario usuario);
}

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

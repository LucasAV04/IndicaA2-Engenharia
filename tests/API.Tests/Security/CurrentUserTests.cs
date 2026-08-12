using API.Security;
using Microsoft.AspNetCore.Http;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace API.Tests.Security;

public sealed class CurrentUserTests
{
    [Fact]
    public void UserId_QuandoSubForGuidValido_DeveRetornarIdentidade()
    {
        var userId = Guid.NewGuid();
        var currentUser = CriarCurrentUser(userId.ToString(), AuthorizationRoles.Usuario);

        Assert.Equal(userId, currentUser.UserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sub-invalido")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void UserId_QuandoSubNaoForGuidValidoENaoVazio_DeveRetornarNulo(string? subject)
    {
        var currentUser = CriarCurrentUser(subject, AuthorizationRoles.Usuario);

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void IsAdministrator_QuandoAdministradorPossuirSubValido_DeveRetornarVerdadeiro()
    {
        var currentUser = CriarCurrentUser(Guid.NewGuid().ToString(), AuthorizationRoles.Administrador);

        Assert.True(currentUser.IsAdministrator);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sub-invalido")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void IsAdministrator_QuandoAdministradorNaoPossuirSubValido_DeveRetornarFalso(string? subject)
    {
        var currentUser = CriarCurrentUser(subject, AuthorizationRoles.Administrador);

        Assert.False(currentUser.IsAdministrator);
    }

    [Fact]
    public void CanAccessUser_QuandoDestinoForGuidVazio_DeveRetornarFalso()
    {
        var currentUser = CriarCurrentUser(Guid.NewGuid().ToString(), AuthorizationRoles.Administrador);

        Assert.False(currentUser.CanAccessUser(Guid.Empty));
    }

    private static CurrentUser CriarCurrentUser(string? subject, string role)
    {
        var claims = new List<Claim> { new("role", role) };
        if (subject is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };

        return new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });
    }
}

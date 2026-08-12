using API.Authorization;
using API.Security;
using Application.DTOs.Indicacao;
using Application.DTOs.Vistoria;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace API.Tests.Authorization;

public sealed class OwnershipAuthorizationHandlerTests
{
    [Fact]
    public async Task IndicacaoOwnerOrAdmin_QuandoUsuarioForOwner_DeveAutorizar()
    {
        var ownerId = Guid.NewGuid();
        var context = CriarContexto(
            new IndicacaoOwnerOrAdminRequirement(),
            CriarIndicacao(ownerId),
            CriarCurrentUser(ownerId, AuthorizationRoles.Usuario));

        await new IndicacaoOwnerOrAdminHandler(CriarCurrentUser(ownerId, AuthorizationRoles.Usuario)).HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task IndicacaoOwnerOrAdmin_QuandoUsuarioForOutro_DeveNegar()
    {
        var ownerId = Guid.NewGuid();
        var context = CriarContexto(
            new IndicacaoOwnerOrAdminRequirement(),
            CriarIndicacao(ownerId),
            CriarCurrentUser(Guid.NewGuid(), AuthorizationRoles.Usuario));

        await new IndicacaoOwnerOrAdminHandler(CriarCurrentUser(Guid.NewGuid(), AuthorizationRoles.Usuario)).HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task IndicacaoOwnerOrAdmin_QuandoAdministrador_DeveAutorizar()
    {
        var currentUser = CriarCurrentUser(Guid.NewGuid(), AuthorizationRoles.Administrador);
        var context = CriarContexto(new IndicacaoOwnerOrAdminRequirement(), CriarIndicacao(Guid.NewGuid()), currentUser);

        await new IndicacaoOwnerOrAdminHandler(currentUser).HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task IndicacaoOwnerOrAdmin_QuandoSubForInvalido_DeveNegar()
    {
        var currentUser = CriarCurrentUser(null, AuthorizationRoles.Usuario);
        var context = CriarContexto(new IndicacaoOwnerOrAdminRequirement(), CriarIndicacao(Guid.NewGuid()), currentUser);

        await new IndicacaoOwnerOrAdminHandler(currentUser).HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task VistoriaOwnerOrAdmin_QuandoUsuarioForOwner_DeveAutorizar()
    {
        var ownerId = Guid.NewGuid();
        var currentUser = CriarCurrentUser(ownerId, AuthorizationRoles.Usuario);
        var context = CriarContexto(new VistoriaOwnerOrAdminRequirement(), CriarVistoria(ownerId), currentUser);

        await new VistoriaOwnerOrAdminHandler(currentUser).HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task VistoriaOwnerOrAdmin_QuandoUsuarioForOutro_DeveNegar()
    {
        var currentUser = CriarCurrentUser(Guid.NewGuid(), AuthorizationRoles.Usuario);
        var context = CriarContexto(new VistoriaOwnerOrAdminRequirement(), CriarVistoria(Guid.NewGuid()), currentUser);

        await new VistoriaOwnerOrAdminHandler(currentUser).HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task VistoriaOwnerOrAdmin_QuandoAdministrador_DeveAutorizar()
    {
        var currentUser = CriarCurrentUser(Guid.NewGuid(), AuthorizationRoles.Administrador);
        var context = CriarContexto(new VistoriaOwnerOrAdminRequirement(), CriarVistoria(Guid.NewGuid()), currentUser);

        await new VistoriaOwnerOrAdminHandler(currentUser).HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task VistoriaOwnerOrAdmin_QuandoSubForInvalido_DeveNegar()
    {
        var currentUser = CriarCurrentUser(null, AuthorizationRoles.Usuario);
        var context = CriarContexto(new VistoriaOwnerOrAdminRequirement(), CriarVistoria(Guid.NewGuid()), currentUser);

        await new VistoriaOwnerOrAdminHandler(currentUser).HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext CriarContexto<TRequirement>(
        TRequirement requirement,
        object resource,
        ICurrentUser currentUser)
        where TRequirement : IAuthorizationRequirement =>
        new([requirement], CriarPrincipal(currentUser), resource);

    private static ICurrentUser CriarCurrentUser(Guid? userId, string role)
    {
        var claims = new List<Claim> { new("role", role) };
        if (userId is Guid id)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, id.ToString()));

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };

        return new CurrentUser(new HttpContextAccessor { HttpContext = context });
    }

    private static ClaimsPrincipal CriarPrincipal(ICurrentUser currentUser) =>
        new(new ClaimsIdentity(authenticationType: currentUser.IsAuthenticated ? "Bearer" : null));

    private static IndicacaoResponseDto CriarIndicacao(Guid ownerId) => new()
    {
        Id = Guid.NewGuid(),
        UsuarioIndicadorId = ownerId,
        NomeIndicada = "Ana",
        TelefoneIndicada = "85999999999",
        CodigoIndicacaoUsado = "A2-123",
        Status = StatusIndicacao.Pendente
    };

    private static VistoriaResponseDto CriarVistoria(Guid ownerId) => new()
    {
        Id = Guid.NewGuid(),
        UsuarioId = ownerId,
        TipoPlanta = "Apartamento",
        AreaM2 = 70,
        Pacote = PacoteVistoria.Simples,
        Status = StatusVistoria.Agendada
    };
}

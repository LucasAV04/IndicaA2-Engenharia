using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Application.Interfaces.Security;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Infrastructure.Tests.Security;

public sealed class JwtAccessTokenGeneratorTests
{
    [Fact]
    public void Generate_DeveEmitirTokenAssinadoComClaimsEsperadasSemDadosSensiveis()
    {
        const string chave = "chave-ficticia-de-testes-com-mais-de-trinta-e-dois-bytes";
        var options = CriarOptions(chave);
        var usuario = new Usuario(
            "Ana",
            "ana@exemplo.com",
            "hash-secreto",
            telefone: "85999999999",
            tipoUsuario: TipoUsuario.Administrador);
        usuario.RegistrarLogin();

        var result = new JwtAccessTokenGenerator(options).Generate(usuario);
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        var principal = handler.ValidateToken(
            result.Token,
            CriarParametrosValidacao(chave),
            out var tokenValidado);
        var token = handler.ReadJwtToken(result.Token);

        Assert.Equal("IndicA2.Tests", token.Issuer);
        Assert.Contains(token.Audiences, audience => audience == "IndicA2.Tests");
        Assert.Contains(token.Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == usuario.Id.ToString());
        Assert.Contains(token.Claims, claim => claim.Type == JwtRegisteredClaimNames.Email && claim.Value == usuario.Email);
        Assert.Contains(token.Claims, claim => claim.Type == "name" && claim.Value == usuario.Nome);
        Assert.Contains(token.Claims, claim => claim.Type == "role" && claim.Value == "Administrador");
        Assert.Equal(usuario.Nome, principal.Identity?.Name);
        Assert.True(principal.IsInRole("Administrador"));
        Assert.Equal(usuario.Id.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(usuario.Email, principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
        Assert.True(tokenValidado.ValidTo > DateTime.UtcNow);
        Assert.InRange(result.ExpiresAtUtc, tokenValidado.ValidTo, tokenValidado.ValidTo.AddSeconds(1));
        Assert.DoesNotContain(token.Claims, claim => claim.Value == usuario.SenhaHash);
        Assert.DoesNotContain(token.Claims, claim => claim.Value == "senha-original");
        Assert.DoesNotContain(token.Claims, claim => claim.Value == usuario.Telefone);
        Assert.DoesNotContain(token.Claims, claim => claim.Value == usuario.UltimoLogin?.ToString("O"));
        Assert.DoesNotContain(token.Claims, claim => claim.Type is "senha" or "senha_hash" or "telefone" or "ultimo_login");
    }

    [Fact]
    public void Generate_DeveRejeitarTokenQuandoAChaveDeValidacaoForIncorreta()
    {
        const string chaveCorreta = "chave-ficticia-de-testes-com-mais-de-trinta-e-dois-bytes";
        const string chaveIncorreta = "chave-incorreta-de-testes-com-mais-de-trinta-e-dois-bytes";
        var usuario = new Usuario("Ana", "ana@exemplo.com", "hash-secreto", codigoIndicacao: "A1B2C3D4");
        var result = new JwtAccessTokenGenerator(CriarOptions(chaveCorreta)).Generate(usuario);
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };

        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(result.Token, CriarParametrosValidacao(chaveIncorreta), out _));
    }

    private static IOptions<JwtOptions> CriarOptions(string chave) =>
        Options.Create(new JwtOptions
        {
            Issuer = "IndicA2.Tests",
            Audience = "IndicA2.Tests",
            Key = chave,
            ExpirationMinutes = 60
        });

    private static TokenValidationParameters CriarParametrosValidacao(string chave) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = "IndicA2.Tests",
            ValidateAudience = true,
            ValidAudience = "IndicA2.Tests",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "name",
            RoleClaimType = "role"
        };
}

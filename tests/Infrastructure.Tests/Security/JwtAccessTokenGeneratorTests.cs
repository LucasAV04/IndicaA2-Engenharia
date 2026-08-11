using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Application.Interfaces.Security;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace Infrastructure.Tests.Security;

public sealed class JwtAccessTokenGeneratorTests
{
    [Fact]
    public void Generate_DeveEmitirClaimsEsperadasSemDadosSensiveis()
    {
        var options = Options.Create(new JwtOptions { Issuer = "IndicA2.Tests", Audience = "IndicA2.Tests", Key = "chave-ficticia-de-testes-com-mais-de-trinta-e-dois-bytes", ExpirationMinutes = 60 });
        var usuario = new Usuario("Ana", "ana@exemplo.com", "hash-secreto", tipoUsuario: TipoUsuario.Administrador);

        var result = new JwtAccessTokenGenerator(options).Generate(usuario);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal("IndicA2.Tests", token.Issuer);
        Assert.Contains(token.Audiences, audience => audience == "IndicA2.Tests");
        Assert.Contains(token.Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == usuario.Id.ToString());
        Assert.Contains(token.Claims, claim => claim.Type == JwtRegisteredClaimNames.Email && claim.Value == usuario.Email);
        Assert.Contains(token.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == "Administrador");
        Assert.DoesNotContain(token.Claims, claim => claim.Value.Contains("hash-secreto", StringComparison.Ordinal));
    }
}

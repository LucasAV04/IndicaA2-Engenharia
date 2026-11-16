using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using API.Security;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace API.Tests.Integration;

public sealed class ContaSegurancaPipelineTests(ApiTestWebApplicationFactory factory)
    : IClassFixture<ApiTestWebApplicationFactory>
{
    [Fact]
    public async Task RecuperacaoSemEmailHabilitadoUsaRespostaGenericaENoStore()
    {
        using var client = factory.CreateHttpsClient();
        using var a = await client.PostAsJsonAsync("/api/auth/esqueci-senha", new { Email = "a@example.invalid" });
        using var b = await client.PostAsJsonAsync("/api/auth/esqueci-senha", new { Email = "b@example.invalid" });
        Assert.Equal(HttpStatusCode.Accepted, a.StatusCode);
        Assert.Equal(a.StatusCode, b.StatusCode);
        Assert.True(a.Headers.CacheControl?.NoStore);
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CorpoDesconhecidoETokenDeQuerySaoRejeitadosSemVazamento()
    {
        using var client = factory.CreateHttpsClient();
        using var unknown = await client.PostAsJsonAsync("/api/auth/esqueci-senha",
            new { Email = "a@example.invalid", marcadorPrivado = "conteudo-ficticio" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.DoesNotContain("marcadorPrivado", await unknown.Content.ReadAsStringAsync());
        using var reset = await client.PostAsJsonAsync("/api/auth/redefinir-senha?token=" + new string('a', 64),
            new { Senha = "ficticia-nova-123" });
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.True(reset.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task RelatoriosExigemAdministrador()
    {
        using var client = factory.CreateHttpsClient();
        using var anonymous = await client.GetAsync("/api/admin/relatorios");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken(Guid.NewGuid(), "1", "Usuario"));
        using var regular = await client.GetAsync("/api/admin/relatorios/csv");
        Assert.Equal(HttpStatusCode.Forbidden, regular.StatusCode);
    }

    [Fact]
    public async Task SessaoValidaExigeUsuarioAtivoRoleEVersaoCorrespondentes()
    {
        var admin = new Usuario("Fictício", "teste@example.invalid", "hash-ficticio", tipoUsuario: TipoUsuario.Administrador);
        var users = new Mock<IUsuarioRepository>(MockBehavior.Strict);
        users.Setup(u => u.ObterPorIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IUsuarioRepository>(_ => users.Object);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.EventsType = typeof(VerificarSessaoJwt));
        }));
        using var client = app.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.OK, await Consultar("1", "Administrador"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Consultar("2", "Administrador"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Consultar("1", "Usuario"));
        admin.Bloquear();
        Assert.Equal(HttpStatusCode.Unauthorized, await Consultar("1", "Administrador"));

        async Task<HttpStatusCode> Consultar(string versao, string role)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken(admin.Id, versao, role));
            using var result = await client.GetAsync("/api/minha-conta/seguranca");
            return result.StatusCode;
        }
    }

    private static string CriarToken(Guid usuario, string versao, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            "chave-ficticia-de-autorizacao-com-mais-de-trinta-e-dois-bytes"));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("IndicA2.Api.Tests",
            "IndicA2.Api.Tests.Client",
            [new Claim(JwtRegisteredClaimNames.Sub, usuario.ToString()), new Claim("role", role), new Claim("cv", versao)],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }
}

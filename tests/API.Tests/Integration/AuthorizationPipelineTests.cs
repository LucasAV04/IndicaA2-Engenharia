using Application.DTOs.Indicacao;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;

namespace API.Tests.Integration;

public sealed class AuthorizationPipelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Issuer = "IndicA2.Authorization.Tests";
    private const string Audience = "IndicA2.Authorization.Tests.Client";
    private const string Key = "chave-ficticia-de-autorizacao-com-mais-de-trinta-e-dois-bytes";
    private readonly WebApplicationFactory<Program> _factory;

    static AuthorizationPipelineTests()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Server=localhost;Database=indicaa2_test;User Id=test;Password=test;");
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__Key", Key);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "60");
    }

    public AuthorizationPipelineTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task EndpointProtegido_SemBearer_DeveRetornarUnauthorizedSemExecutarController()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/indicacoes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EndpointAdministrativo_ComTokenDeUsuario_DeveRetornarForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Usuario"));

        var response = await client.GetAsync("/api/indicacoes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sub-invalido")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task EndpointAdministrativo_ComAdministradorSemSubValido_DeveRetornarForbidden(string? subject)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            CriarToken("Administrador", subject, includeSubject: subject is not null));

        var response = await client.GetAsync("/api/indicacoes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EndpointAdministrativo_ComAdministradorESubValido_DeveAutorizarSemConectarAoMySql()
    {
        var service = new Mock<IIndicacaoService>();
        service.Setup(item => item.ObterTodasAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IndicacaoResponseDto>());
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIndicacaoService>();
                services.AddScoped(_ => service.Object);
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.GetAsync("/api/indicacoes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        service.Verify(item => item.ObterTodasAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_SemBearer_DevePermanecerForaDaExigenciaDeAutenticacao()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/auth/login", content: null);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_DeveAplicarBearerSomenteAsOperacoesProtegidas()
    {
        using var client = _factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        var login = paths.GetProperty("/api/auth/login").GetProperty("post");
        var indicacoes = paths.GetProperty("/api/indicacoes").GetProperty("get");
        var criarVistoria = paths.GetProperty("/api/vistorias").GetProperty("post");

        Assert.False(login.TryGetProperty("security", out _));
        AssertBearer(indicacoes);
        AssertBearer(criarVistoria);
        Assert.Equal("http", document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer")
            .GetProperty("type")
            .GetString());
    }

    private static void AssertBearer(JsonElement operation)
    {
        var security = operation.GetProperty("security");
        Assert.Equal(JsonValueKind.Array, security.ValueKind);
        Assert.True(security.GetArrayLength() > 0);
        Assert.True(security[0].TryGetProperty("Bearer", out _));
    }

    private static string CriarToken(string role, string? subject = null, bool includeSubject = true)
    {
        var claims = new List<Claim>
        {
            new Claim("name", "Usuário de teste"),
            new Claim("role", role)
        };
        if (includeSubject)
            claims.Insert(0, new Claim(JwtRegisteredClaimNames.Sub, subject ?? Guid.NewGuid().ToString()));
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

using Application.DTOs.Cashback;
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

public sealed class CashbackAuthorizationPipelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Issuer = "IndicA2.Authorization.Tests";
    private const string Audience = "IndicA2.Authorization.Tests.Client";
    private const string Key = "chave-ficticia-de-autorizacao-com-mais-de-trinta-e-dois-bytes";
    private readonly WebApplicationFactory<Program> _factory;

    static CashbackAuthorizationPipelineTests()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Server=localhost;Database=indicaa2_test;User Id=test;Password=test;");
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__Key", Key);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "60");
    }

    public CashbackAuthorizationPipelineTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

    [Fact]
    public async Task Cashback_SemBearer_DeveRetornarUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/cashbacks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cashback_ComTokenDeUsuario_DeveRetornarForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Usuario"));

        var response = await client.GetAsync("/api/cashbacks");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cashback_ComAdministrador_DeveRetornarOkSemConectarAoMySql()
    {
        var service = new Mock<ICashbackService>();
        service.Setup(item => item.ObterTodosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CashbackResponseDto>());
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.GetAsync("/api/cashbacks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        service.Verify(item => item.ObterTodosAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GerarPorPagamento_ComAdministrador_DeveRetornarLocationParaConsultaPorId()
    {
        var pagamentoVistoriaId = Guid.NewGuid();
        var cashback = new CashbackResponseDto { Id = Guid.NewGuid() };
        var service = new Mock<ICashbackService>();
        service.Setup(item => item.GerarPorPagamentoAsync(pagamentoVistoriaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cashback);
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.PostAsync($"/api/cashbacks/por-pagamento/{pagamentoVistoriaId}", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"/api/cashbacks/{cashback.Id}", response.Headers.Location!.AbsolutePath);
        service.Verify(item => item.GerarPorPagamentoAsync(pagamentoVistoriaId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenApi_DeveDocumentarEndpointsDeCashbackSemFluxoDePagamento()
    {
        using var client = _factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/cashbacks", out _));
        Assert.True(paths.TryGetProperty("/api/cashbacks/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/cashbacks/por-pagamento/{pagamentoVistoriaId}", out _));
        Assert.True(paths.TryGetProperty("/api/cashbacks/por-indicador/{usuarioIndicadorId}", out _));
        Assert.True(paths.TryGetProperty("/api/cashbacks/{id}/aprovar", out _));
        Assert.True(paths.TryGetProperty("/api/cashbacks/{id}/cancelar", out _));
        Assert.False(paths.TryGetProperty("/api/cashbacks/{id}/pagar", out _));
        Assert.DoesNotContain(paths.EnumerateObject(), path => path.Name.Contains("pix", StringComparison.OrdinalIgnoreCase));
    }

    private WebApplicationFactory<Program> CriarFactory(ICashbackService service) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICashbackService>();
            services.AddScoped(_ => service);
        }));

    private static string CriarToken(string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("name", "Administrador de teste"),
            new Claim("role", role)
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, Audience, claims, expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

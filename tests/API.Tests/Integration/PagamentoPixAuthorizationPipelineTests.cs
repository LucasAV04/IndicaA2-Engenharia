using Application.DTOs.PagamentoPix;
using Application.Interfaces.Services;
using Domain.Enums;
using Domain.Exceptions;
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

public sealed class PagamentoPixAuthorizationPipelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Issuer = "IndicA2.Api.Tests";
    private const string Audience = "IndicA2.Api.Tests.Client";
    private const string Key = "chave-ficticia-de-autorizacao-com-mais-de-trinta-e-dois-bytes";
    private readonly WebApplicationFactory<Program> _factory;

    static PagamentoPixAuthorizationPipelineTests()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Server=localhost;Database=indicaa2_test;User Id=test;Password=test;");
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__Key", Key);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "60");
    }

    public PagamentoPixAuthorizationPipelineTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

    [Fact]
    public async Task PagamentosPix_SemBearer_DeveRetornarUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/pagamentos-pix/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PagamentosPix_ComTokenDeUsuario_DeveRetornarForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Usuario"));

        var response = await client.GetAsync($"/api/pagamentos-pix/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CriarPorCashback_ComAdministrador_DeveRetornarCreatedLocationESemChavePix()
    {
        var cashbackId = Guid.NewGuid();
        var pagamentoPix = CriarResposta();
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.CriarPorCashbackAsync(cashbackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamentoPix);
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.PostAsync($"/api/pagamentos-pix/por-cashback/{cashbackId}", content: null);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"/api/pagamentos-pix/{pagamentoPix.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(pagamentoPix.Id, document.RootElement.GetProperty("id").GetGuid());
        Assert.False(document.RootElement.TryGetProperty("chavePix", out _));
        Assert.DoesNotContain("ciphertext", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nonce", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tag", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encryptionVersion", json, StringComparison.OrdinalIgnoreCase);
        service.Verify(item => item.CriarPorCashbackAsync(cashbackId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consultas_ComAdministrador_DevemRetornarOk()
    {
        var pagamentoPix = CriarResposta();
        IReadOnlyCollection<PagamentoPixResponseDto> pagamentosPix = [pagamentoPix];
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.ObterPorIdAsync(pagamentoPix.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pagamentoPix);
        service.Setup(item => item.ObterPorCashbackIdAsync(pagamentoPix.CashbackId, It.IsAny<CancellationToken>())).ReturnsAsync(pagamentoPix);
        service.Setup(item => item.ObterPorUsuarioBeneficiarioIdAsync(
                pagamentoPix.UsuarioBeneficiarioId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamentosPix);
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var porId = await client.GetAsync($"/api/pagamentos-pix/{pagamentoPix.Id}");
        var porCashback = await client.GetAsync($"/api/pagamentos-pix/por-cashback/{pagamentoPix.CashbackId}");
        var porBeneficiario = await client.GetAsync($"/api/pagamentos-pix/por-beneficiario/{pagamentoPix.UsuarioBeneficiarioId}");

        Assert.Equal(HttpStatusCode.OK, porId.StatusCode);
        Assert.Equal(HttpStatusCode.OK, porCashback.StatusCode);
        Assert.Equal(HttpStatusCode.OK, porBeneficiario.StatusCode);
    }

    [Fact]
    public async Task ObterPorId_QuandoPagamentoNaoExistir_DeveRetornarNotFound()
    {
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PagamentoPixNaoEncontradoException());
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.GetAsync($"/api/pagamentos-pix/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ObterPorCashback_QuandoPagamentoNaoExistir_DeveRetornarNotFound()
    {
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.ObterPorCashbackIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PagamentoPixNaoEncontradoException());
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.GetAsync($"/api/pagamentos-pix/por-cashback/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CriarPorCashback_QuandoRegraDeDominioForViolada_DeveRetornarUnprocessableEntity()
    {
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.CriarPorCashbackAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PagamentoPixJaExisteException());
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.PostAsync($"/api/pagamentos-pix/por-cashback/{Guid.NewGuid()}", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Cancelar_ComAdministrador_DeveRetornarNoContent()
    {
        var id = Guid.NewGuid();
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.CancelarAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.PatchAsync($"/api/pagamentos-pix/{id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        service.Verify(item => item.CancelarAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancelar_QuandoTransicaoForInvalida_DeveRetornarUnprocessableEntity()
    {
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.CancelarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransicaoPagamentoPixInvalidaException("cancelar", "Processando"));
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.PatchAsync($"/api/pagamentos-pix/{Guid.NewGuid()}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Cancelar_QuandoPagamentoNaoExistir_DeveRetornarNotFound()
    {
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.CancelarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PagamentoPixNaoEncontradoException());
        using var factory = CriarFactory(service.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CriarToken("Administrador"));

        var response = await client.PatchAsync($"/api/pagamentos-pix/{Guid.NewGuid()}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_DeveDocumentarEndpointsAdministrativosEProtegidosSemProcessamento()
    {
        using var client = _factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");

        AssertBearer(paths.GetProperty("/api/pagamentos-pix/por-cashback/{cashbackId}").GetProperty("post"));
        AssertBearer(paths.GetProperty("/api/pagamentos-pix/{id}").GetProperty("get"));
        AssertBearer(paths.GetProperty("/api/pagamentos-pix/por-cashback/{cashbackId}").GetProperty("get"));
        AssertBearer(paths.GetProperty("/api/pagamentos-pix/por-beneficiario/{usuarioId}").GetProperty("get"));
        AssertBearer(paths.GetProperty("/api/pagamentos-pix/{id}/cancelar").GetProperty("patch"));
        Assert.DoesNotContain(paths.EnumerateObject(), path =>
            path.Name.Contains("/processar", StringComparison.OrdinalIgnoreCase) ||
            path.Name.Contains("/enviar", StringComparison.OrdinalIgnoreCase) ||
            path.Name.Contains("/pagar", StringComparison.OrdinalIgnoreCase) ||
            path.Name.Contains("/confirmar", StringComparison.OrdinalIgnoreCase) ||
            path.Name.Contains("/reprocessar", StringComparison.OrdinalIgnoreCase));
    }

    private WebApplicationFactory<Program> CriarFactory(IPagamentoPixService service) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPagamentoPixService>();
            services.AddScoped(_ => service);
        }));

    private static void AssertBearer(JsonElement operation)
    {
        var security = operation.GetProperty("security");
        Assert.Equal(JsonValueKind.Array, security.ValueKind);
        Assert.True(security.GetArrayLength() > 0);
        Assert.True(security[0].TryGetProperty("Bearer", out _));
    }

    private static PagamentoPixResponseDto CriarResposta() => new()
    {
        Id = Guid.NewGuid(),
        CashbackId = Guid.NewGuid(),
        UsuarioBeneficiarioId = Guid.NewGuid(),
        Valor = 100m,
        TipoChavePix = TipoChavePix.Email,
        Status = StatusPagamentoPix.Pendente,
        QuantidadeTentativas = 0,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static string CriarToken(string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("name", "Administrador de teste"),
            new Claim("role", role)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

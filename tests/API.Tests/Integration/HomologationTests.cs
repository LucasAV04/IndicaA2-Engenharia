using System.Net;
using System.Security.Cryptography;
using API.Homologation;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace API.Tests.Integration;

public sealed class HomologationTests(ApiTestWebApplicationFactory factory)
    : IClassFixture<ApiTestWebApplicationFactory>
{
    [Fact]
    public void ValidPrivateConfigurationIsAccepted()
        => HomologationEnvironment.Validate(CreateConfiguration());

    [Theory]
    [InlineData("Jwt:Key", "")]
    [InlineData("Jwt:Key", "placeholder-placeholder-placeholder-placeholder")]
    [InlineData("Jwt:Key", "short")]
    [InlineData("PublicWeb:BaseUrl", "http://localhost:8443")]
    [InlineData("PublicWeb:BaseUrl", "https://example.invalid")]
    [InlineData("ConnectionStrings:DefaultConnection", "Server=other;Database=indicaa2_homologacao")]
    [InlineData("ConnectionStrings:DefaultConnection", "Server=mysql;Database=another")]
    [InlineData("PagamentoPix:ProcessamentoWorker:Habilitado", "true")]
    [InlineData("RecebimentoPix:ProcessamentoWorker:Habilitado", "true")]
    [InlineData("CashbackPagamentoPreparacaoWorker:Habilitado", "true")]
    [InlineData("RecebimentoPix:Habilitado", "true")]
    [InlineData("EfiPix:BaseUrl", "https://example.invalid")]
    public void UnsafeConfigurationIsRejected(string key, string value)
    {
        var config = CreateConfiguration();
        config[key] = value;
        Assert.Throws<InvalidOperationException>(() => HomologationEnvironment.Validate(config));
    }

    [Fact]
    public void ReusedEncryptionKeyIsRejected()
    {
        var config = CreateConfiguration();
        config["Homologation:ChargeKey"] = config["DadosPixEncryption:Key"];
        Assert.Throws<InvalidOperationException>(() => HomologationEnvironment.Validate(config));
    }

    [Fact]
    public async Task LiveIsAnonymousAndIndependentOfDatabase()
    {
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("""{"status":"ok"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnavailableDatabaseReturnsOnlyGenericStatus()
    {
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("""{"status":"unavailable"}""", await response.Content.ReadAsStringAsync());
    }

    private static IConfigurationRoot CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicWeb:BaseUrl"] = "https://localhost:8443",
            ["ConnectionStrings:DefaultConnection"] =
                $"Server=mysql;Database=indicaa2_homologacao;User ID=indicaa2;Password={Secret()}",
            ["Jwt:Key"] = Secret(),
            ["DadosPixEncryption:Key"] = Secret(),
            ["Homologation:ChargeKey"] = Secret(),
            ["Homologation:CertificatePassword"] = Secret()
        }).Build();

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}

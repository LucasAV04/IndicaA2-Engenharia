using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace API.Tests.Integration;

public sealed class ApiTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly IReadOnlyDictionary<string, string?> TestConfiguration =
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Server=localhost;Database=indicaa2_test;User Id=test;Password=test;",
            ["Jwt:Issuer"] = "IndicA2.Api.Tests",
            ["Jwt:Audience"] = "IndicA2.Api.Tests.Client",
            ["Jwt:Key"] = "chave-ficticia-de-autorizacao-com-mais-de-trinta-e-dois-bytes",
            ["Jwt:ExpirationMinutes"] = "60"
        };

    static ApiTestWebApplicationFactory()
    {
        foreach (var (key, value) in TestConfiguration)
            Environment.SetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal), value);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(TestConfiguration));
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
            services.AddDataProtection().UseEphemeralDataProtectionProvider());
    }
}

internal static class ApiTestWebApplicationFactoryExtensions
{
    private static readonly Uri HttpsBaseAddress = new("https://localhost");

    public static HttpClient CreateHttpsClient(this WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = HttpsBaseAddress
        });
}

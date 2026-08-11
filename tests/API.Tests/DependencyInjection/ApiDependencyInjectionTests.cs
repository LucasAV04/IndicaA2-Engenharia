using Application.Interfaces.Services;
using Application.Services;
using Domain.Interfaces;
using Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.DependencyInjection;

public sealed class ApiDependencyInjectionTests
{
    [Fact]
    public void Container_ComConfiguracaoValida_DeveResolverServicesERepositoriesSemAbrirConexao()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=indicaa2_test;User Id=test;Password=test;"
                , ["Jwt:Issuer"] = "IndicA2.Tests"
                , ["Jwt:Audience"] = "IndicA2.Tests"
                , ["Jwt:Key"] = "chave-ficticia-de-testes-com-mais-de-trinta-e-dois-bytes"
                , ["Jwt:ExpirationMinutes"] = "60"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddScoped<IIndicacaoService, IndicacaoService>();
        services.AddScoped<IVistoriaService, VistoriaService>();
        services.AddScoped<IAuthService, AuthService>();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var indicacaoService = scope.ServiceProvider.GetRequiredService<IIndicacaoService>();
        var vistoriaService = scope.ServiceProvider.GetRequiredService<IVistoriaService>();
        var vistoriaRepository = scope.ServiceProvider.GetRequiredService<IVistoriaRepository>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        Assert.IsType<IndicacaoService>(indicacaoService);
        Assert.IsType<VistoriaService>(vistoriaService);
        Assert.NotNull(vistoriaRepository);
        Assert.IsType<AuthService>(authService);
    }
}

using Domain.Interfaces;
using Application.Interfaces.Security;
using Infrastructure.DependencyInjection;
using Infrastructure.Repositories;
using Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Infrastructure.Tests.DependencyInjection;

public sealed class InfrastructureDependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_DeveRegistrarIUsuarioRepositoryComoScoped()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=indica_a2;User ID=test;Password=test;"
                , ["Jwt:Issuer"] = "IndicA2.Tests"
                , ["Jwt:Audience"] = "IndicA2.Tests"
                , ["Jwt:Key"] = "chave-ficticia-de-testes-com-mais-de-trinta-e-dois-bytes"
                , ["Jwt:ExpirationMinutes"] = "60"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();

        Assert.IsType<UsuarioMySqlRepository>(repository);
    }

    [Fact]
    public void AddInfrastructure_DeveRegistrarIVistoriaRepositoryComoScoped()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=indica_a2;User ID=test;Password=test;"
                , ["Jwt:Issuer"] = "IndicA2.Tests"
                , ["Jwt:Audience"] = "IndicA2.Tests"
                , ["Jwt:Key"] = "chave-ficticia-de-testes-com-mais-de-trinta-e-dois-bytes"
                , ["Jwt:ExpirationMinutes"] = "60"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IVistoriaRepository>();

        Assert.IsType<VistoriaMySqlRepository>(repository);
    }

    [Fact]
    public void AddInfrastructure_DeveRegistrarIPagamentoVistoriaRepositoryComoScoped()
    {
        var configuration = CriarConfiguration();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPagamentoVistoriaRepository>();

        Assert.IsType<PagamentoVistoriaMySqlRepository>(repository);
    }

    [Fact]
    public void AddInfrastructure_DeveRegistrarICashbackRepositoryComoScoped()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(CriarConfiguration());

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICashbackRepository>();

        Assert.IsType<CashbackMySqlRepository>(repository);
    }

    [Fact]
    public void AddInfrastructure_DeveRegistrarGeradorDeCodigoIndicacaoComoScoped()
    {
        var configuration = CriarConfiguration();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<ICodigoIndicacaoGenerator>();

        Assert.IsType<Infrastructure.Security.CodigoIndicacaoGenerator>(generator);
    }

    [Fact]
    public void AddInfrastructure_DeveRegistrarDadosPixRepositoryEProtectorComChaveFicticia()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(CriarConfiguration());

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IDadosPixRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<IDadosPixProtector>();

        Assert.IsType<DadosPixMySqlRepository>(repository);
        Assert.IsType<AesGcmDadosPixProtector>(protector);
    }

    private static IConfiguration CriarConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=indica_a2;User ID=test;Password=test;",
            ["Jwt:Issuer"] = "IndicA2.Tests",
            ["Jwt:Audience"] = "IndicA2.Tests",
            ["Jwt:Key"] = "chave-ficticia-de-testes-com-mais-de-trinta-e-dois-bytes",
            ["Jwt:ExpirationMinutes"] = "60",
            ["DadosPixEncryption:Key"] = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA="
        })
        .Build();
}

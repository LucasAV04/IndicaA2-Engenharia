using Application.Interfaces.Services;
using Application.Services;
using Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.DependencyInjection;

public sealed class ApiDependencyInjectionTests
{
    [Fact]
    public void Container_ComConfiguracaoValida_DeveResolverIIndicacaoService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=indicaa2_test;User Id=test;Password=test;"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddScoped<IIndicacaoService, IndicacaoService>();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IIndicacaoService>();

        Assert.IsType<IndicacaoService>(service);
    }
}

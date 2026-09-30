using Application.Recebimentos;
using Infrastructure.DependencyInjection;
using Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Infrastructure.Tests;

public sealed class RecebimentoPixDependencyInjectionTests
{
    [Fact]
    public void DesabilitadoRegistraLifetimesSemExigirSegredosOuCriarHandler()
    {
        var services=new ServiceCollection();
        services.AddRecebimentoPix(new ConfigurationBuilder().Build());
        using var container=services.BuildServiceProvider();
        Assert.False(container.GetRequiredService<RecebimentoPixOptions>().Habilitado);
        foreach(var tipo in new[]{typeof(ICobrancaPixVistoriaService),typeof(ICobrancaPixVistoriaStore),
            typeof(IRecebimentoPixProcessamentoService),typeof(IRecebimentoPixWebhookStore),typeof(IRecebimentoPixCandidatoStore),typeof(ICobrancaPixVistoriaProvider)})
            Assert.Equal(ServiceLifetime.Scoped,Assert.Single(services,x=>x.ServiceType==tipo).Lifetime);
    }

    [Fact]
    public void HabilitadoSemConfiguracaoFalhaAntesDeResolverServico()
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["RecebimentoPix:Habilitado"]="true" }).Build();
        Assert.Throws<InvalidOperationException>(()=>new ServiceCollection().AddRecebimentoPix(configuration));
    }

    [Fact]
    public async Task CacheOAuthIsolaEscoposDeEnvioERecebimento()
    {
        var cache=new EfiPixAccessTokenCache(TimeProvider.System);
        var carregados=new List<string>();
        foreach(var scope in new[]{"pix.send","cob.write","cob.read","pix.read","webhook.write","webhook.read"})
        {
            Assert.Equal(scope,await cache.ObterAsync(scope,_=>{ carregados.Add(scope); return Task.FromResult(new EfiPixAccessToken(scope,3600)); },default));
            Assert.Equal(scope,await cache.ObterAsync(scope,_=>throw new InvalidOperationException("nao-recarregar"),default));
        }
        Assert.Equal(6,carregados.Count);
    }
}

using Infrastructure.Tests.Integration;
using Xunit;

namespace Infrastructure.Tests.IntegrationSupport;

public sealed class MySqlIntegrationPreflightTests
{
    [Fact]
    public async Task VerificarAsync_QuandoVariavelEstiverAusente_NaoDeveTentarConexao()
    {
        var probe = new ProbeControlavel();
        var preflight = new MySqlIntegrationPreflight(probe);

        var resultado = await preflight.VerificarAsync(null);

        Assert.False(resultado.Disponivel);
        Assert.Equal(0, probe.QuantidadeChamadas);
    }

    [Fact]
    public async Task VerificarAsync_QuandoForChamadoRepetidamente_DeveCompartilharUmaUnicaSondagem()
    {
        var probe = new ProbeControlavel();
        var preflight = new MySqlIntegrationPreflight(probe);

        var primeira = preflight.VerificarAsync("Server=teste;");
        var segunda = preflight.VerificarAsync("Server=teste;");
        var resultados = await Task.WhenAll(primeira, segunda);

        Assert.All(resultados, resultado => Assert.True(resultado.Disponivel));
        Assert.Equal(1, probe.QuantidadeChamadas);
    }

    [Fact]
    public async Task ExecutarAsync_QuandoPreflightFalhar_NaoDeveIniciarBootstrap()
    {
        var probe = new ProbeControlavel(new InvalidOperationException("falha simulada"));
        var gate = new MySqlIntegrationBootstrapGate(new MySqlIntegrationPreflight(probe));
        var bootstrapExecutado = false;

        var resultado = await gate.ExecutarAsync(
            "Server=teste;",
            _ =>
            {
                bootstrapExecutado = true;
                return Task.CompletedTask;
            });

        Assert.False(resultado);
        Assert.False(bootstrapExecutado);
        Assert.Equal(1, probe.QuantidadeChamadas);
    }

    [Fact]
    public void Marker_QuandoForDaMesmaConexao_DevePermitirReutilizarPreflightSemExporSegredo()
    {
        const string connectionString = "Server=teste;";
        var anterior = Environment.GetEnvironmentVariable(MySqlIntegrationPreflightMarker.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                MySqlIntegrationPreflightMarker.EnvironmentVariable,
                MySqlIntegrationPreflightMarker.Criar(connectionString));

            Assert.True(MySqlIntegrationPreflightMarker.Corresponde(connectionString));
            Assert.False(MySqlIntegrationPreflightMarker.Corresponde("Server=outro;"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(MySqlIntegrationPreflightMarker.EnvironmentVariable, anterior);
        }
    }

    [Fact]
    public void IntegracoesMySql_DevemUsarCategoriaCentralESemReducaoDeCasos()
    {
        var raiz = EncontrarRaizProjeto();
        var diretorio = Path.Combine(raiz, "tests", "Infrastructure.Tests", "Integration");
        var arquivos = Directory.EnumerateFiles(diretorio, "*IntegrationTests.cs")
            .Where(arquivo => File.ReadAllText(arquivo).Contains("[Collection(MySqlIntegrationCollection.Name)]"))
            .ToArray();
        var casos = arquivos.Sum(arquivo => File.ReadAllText(arquivo).Split("[MySqlIntegrationFact]").Length - 1);

        Assert.Equal(13, arquivos.Length);
        Assert.Equal(105, casos);
        Assert.All(arquivos, arquivo => Assert.DoesNotContain("[Fact]", File.ReadAllText(arquivo)));

        Assert.All(arquivos, arquivo => Assert.Contains(
            "[Trait(\"Category\", MySqlIntegrationCategory.Name)]",
            File.ReadAllText(arquivo)));
    }

    private static string EncontrarRaizProjeto()
    {
        for (var diretorio = new DirectoryInfo(AppContext.BaseDirectory); diretorio is not null; diretorio = diretorio.Parent)
        {
            if (File.Exists(Path.Combine(diretorio.FullName, "IndicaA2.slnx")))
                return diretorio.FullName;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do projeto IndicA2.");
    }

    private sealed class ProbeControlavel(Exception? falha = null) : IMySqlIntegrationConnectionProbe
    {
        public int QuantidadeChamadas { get; private set; }

        public Task ProbeAsync(string connectionString, CancellationToken cancellationToken)
        {
            QuantidadeChamadas++;
            return falha is null ? Task.CompletedTask : Task.FromException(falha);
        }
    }
}

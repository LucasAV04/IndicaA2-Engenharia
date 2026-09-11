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
    public void Marker_QuandoUsarTextoOriginalNaoCanonico_DeveSerDeterministico()
    {
        const string connectionString = "server=teste;uid=usuario;";
        var normalizada = new MySqlConnector.MySqlConnectionStringBuilder(connectionString).ConnectionString;
        var anterior = Environment.GetEnvironmentVariable(MySqlIntegrationPreflightMarker.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                MySqlIntegrationPreflightMarker.EnvironmentVariable,
                MySqlIntegrationPreflightMarker.Criar(connectionString));

            Assert.Equal(
                MySqlIntegrationPreflightMarker.Criar(connectionString),
                MySqlIntegrationPreflightMarker.Criar(connectionString));
            Assert.True(MySqlIntegrationPreflightMarker.Corresponde(connectionString));
            Assert.NotEqual(connectionString, normalizada);
            Assert.False(MySqlIntegrationPreflightMarker.Corresponde(normalizada));
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
        Assert.Equal(119, casos);
        Assert.All(arquivos, arquivo => Assert.DoesNotContain("[Fact]", File.ReadAllText(arquivo)));

        Assert.All(arquivos, arquivo => Assert.Contains(
            "[Trait(\"Category\", MySqlIntegrationCategory.Name)]",
            File.ReadAllText(arquivo)));
    }

    [Fact]
    public async Task Script_QuandoVariavelEstiverAusente_DeveRetornarCodigosCorretosSemChamarDotnet()
    {
        var resultadoOpcional = await ExecutarScriptSemVariavelAsync(requireMySql: false);
        Assert.Equal(0, resultadoOpcional.ExitCode);
        Assert.Contains("SKIPPED:", resultadoOpcional.Output);
        Assert.False(resultadoOpcional.DotnetFoiChamado);

        var resultadoObrigatorio = await ExecutarScriptSemVariavelAsync(requireMySql: true);
        Assert.Equal(2, resultadoObrigatorio.ExitCode);
        Assert.Contains("ERROR:", resultadoObrigatorio.Output);
        Assert.False(resultadoObrigatorio.DotnetFoiChamado);
    }

    private static async Task<ResultadoScript> ExecutarScriptSemVariavelAsync(bool requireMySql)
    {
        var raiz = EncontrarRaizProjeto();
        var caminhoPwsh = EncontrarPowerShellSete();
        var pathOriginal = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var diretorioTemporario = Path.Combine(Path.GetTempPath(), $"indicaa2-preflight-{Guid.NewGuid():N}");
        var marcadorDotnet = Path.Combine(diretorioTemporario, "dotnet-foi-chamado.txt");
        Directory.CreateDirectory(diretorioTemporario);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(diretorioTemporario, "dotnet.cmd"),
                $"@echo invoked > \"{marcadorDotnet}\"{Environment.NewLine}exit /b 99");

            var inicio = new System.Diagnostics.ProcessStartInfo(caminhoPwsh)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = raiz
            };
            inicio.ArgumentList.Add("-NoProfile");
            inicio.ArgumentList.Add("-File");
            inicio.ArgumentList.Add(Path.Combine(raiz, "scripts", "Invoke-MySqlIntegrationTests.ps1"));
            if (requireMySql)
                inicio.ArgumentList.Add("-RequireMySql");
            inicio.Environment.Remove(MySqlIntegrationFixture.ConnectionStringEnvironmentVariable);
            inicio.Environment["PATH"] = $"{diretorioTemporario}{Path.PathSeparator}{pathOriginal}";

            using var processo = System.Diagnostics.Process.Start(inicio)
                ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell suportado.");
            var output = await processo.StandardOutput.ReadToEndAsync();
            output += await processo.StandardError.ReadToEndAsync();
            await processo.WaitForExitAsync();

            return new ResultadoScript(processo.ExitCode, output, File.Exists(marcadorDotnet));
        }
        finally
        {
            Directory.Delete(diretorioTemporario, recursive: true);
        }
    }

    private static string EncontrarPowerShellSete()
    {
        var nomeExecutavel = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        var pathOriginal = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var entrada in pathOriginal.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var diretorio = RemoverAspasExternas(entrada.Trim());
            if (string.IsNullOrWhiteSpace(diretorio))
                continue;

            var candidato = Path.Combine(diretorio, nomeExecutavel);
            if (File.Exists(candidato))
                return candidato;
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidato = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
            if (File.Exists(candidato))
                return candidato;
        }

        throw new FileNotFoundException("Não foi possível localizar o PowerShell 7 (pwsh) para o teste de preflight.");
    }

    private static string RemoverAspasExternas(string valor) =>
        valor.Length >= 2 && valor.StartsWith('"') && valor.EndsWith('"')
            ? valor[1..^1]
            : valor;

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

    private sealed record ResultadoScript(int ExitCode, string Output, bool DotnetFoiChamado);
}

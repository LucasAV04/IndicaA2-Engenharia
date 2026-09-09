using MySqlConnector;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Tests.Integration;

internal interface IMySqlIntegrationConnectionProbe
{
    Task ProbeAsync(string connectionString, CancellationToken cancellationToken);
}

internal sealed class MySqlIntegrationConnectionProbe : IMySqlIntegrationConnectionProbe
{
    public async Task ProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT 1;", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}

internal sealed record MySqlIntegrationPreflightResult(bool Disponivel)
{
    public static MySqlIntegrationPreflightResult VariavelAusente() => new(false);

    public static MySqlIntegrationPreflightResult Falhou() => new(false);

    public static MySqlIntegrationPreflightResult Aprovado() => new(true);
}

/// <summary>
/// Compartilha uma única verificação de disponibilidade por processo.
/// A conexão usada aqui é somente para SELECT 1; o bootstrap só começa após sucesso.
/// </summary>
internal sealed class MySqlIntegrationPreflight(IMySqlIntegrationConnectionProbe probe)
{
    private readonly object _sincronizacao = new();
    private Task<MySqlIntegrationPreflightResult>? _resultadoCompartilhado;

    public Task<MySqlIntegrationPreflightResult> VerificarAsync(
        string? connectionString,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return Task.FromResult(MySqlIntegrationPreflightResult.VariavelAusente());

        lock (_sincronizacao)
        {
            return _resultadoCompartilhado ??= ExecutarAsync(connectionString);
        }
    }

    private async Task<MySqlIntegrationPreflightResult> ExecutarAsync(string connectionString)
    {
        try
        {
            await probe.ProbeAsync(connectionString, CancellationToken.None);
            return MySqlIntegrationPreflightResult.Aprovado();
        }
        catch
        {
            return MySqlIntegrationPreflightResult.Falhou();
        }
    }
}

internal sealed class MySqlIntegrationBootstrapGate(MySqlIntegrationPreflight preflight)
{
    public async Task<bool> ExecutarAsync(
        string? connectionString,
        Func<CancellationToken, Task> bootstrap,
        CancellationToken cancellationToken = default)
    {
        var resultado = await preflight.VerificarAsync(connectionString, cancellationToken);
        if (!resultado.Disponivel)
            return false;

        await bootstrap(cancellationToken);
        return true;
    }
}

internal static class MySqlIntegrationPreflightMarker
{
    public const string EnvironmentVariable = "INDICA2_TEST_MYSQL_PREFLIGHT_HASH";

    public static bool Corresponde(string connectionString)
    {
        var marker = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.Equals(marker, Criar(connectionString), StringComparison.Ordinal);
    }

    public static string Criar(string connectionString) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString))).ToLowerInvariant();
}

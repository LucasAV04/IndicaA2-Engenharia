using Infrastructure.Database;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

public sealed class MySqlIntegrationFixture : IAsyncLifetime
{
    public const string ConnectionStringEnvironmentVariable = "INDICA2_TEST_MYSQL_CONNECTION";
    private const string DatabasePrefix = "indicaa2_test_";
    private string? _adminConnectionString;

    public string DatabaseName { get; } = $"{DatabasePrefix}{Guid.NewGuid():N}";

    public MySqlConnectionFactory ConnectionFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"A variavel {ConnectionStringEnvironmentVariable} e obrigatoria para os testes de integracao MySQL.");

        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException(
                $"A variavel {ConnectionStringEnvironmentVariable} deve ser uma conexao administrativa sem database para impedir o uso acidental de banco de desenvolvimento.");
        }

        if (string.IsNullOrWhiteSpace(builder.Server))
            throw new InvalidOperationException("O servidor MySQL da conexao de integracao e obrigatorio.");

        ValidarNomeBanco(DatabaseName);
        _adminConnectionString = builder.ConnectionString;
        builder.Database = DatabaseName;
        ConnectionFactory = new MySqlConnectionFactory(builder.ConnectionString);

        try
        {
            await CriarBancoEAplicarSchemaAsync();
        }
        catch
        {
            await RemoverBancoAsync();
            throw;
        }
    }

    public async Task LimparDadosAsync()
    {
        ValidarNomeBanco(DatabaseName);

        await using var connection = ConnectionFactory.Create();
        await connection.OpenAsync();

        foreach (var sql in new[]
                 {
                     "DELETE FROM indicacoes;",
                     "DELETE FROM vistorias;",
                     "DELETE FROM usuarios;"
                 })
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    public Task DisposeAsync() => RemoverBancoAsync();

    private async Task CriarBancoEAplicarSchemaAsync()
    {
        await using var connection = new MySqlConnection(_adminConnectionString);
        await connection.OpenAsync();

        await ExecutarAsync(connection, $"CREATE DATABASE `{DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");

        var raiz = EncontrarRaizProjeto();
        foreach (var script in new[]
                 {
                     "database/002_create_usuarios.sql",
                     "database/003_create_vistorias.sql",
                     "database/001_create_indicacoes.sql"
                 })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(raiz, script));
            await using var databaseConnection = ConnectionFactory.Create();
            await databaseConnection.OpenAsync();
            await ExecutarAsync(databaseConnection, sql);
        }
    }

    private async Task RemoverBancoAsync()
    {
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
            return;

        ValidarNomeBanco(DatabaseName);
        await using var connection = new MySqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await ExecutarAsync(connection, $"DROP DATABASE IF EXISTS `{DatabaseName}`;");
    }

    private static async Task ExecutarAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string EncontrarRaizProjeto()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IndicaA2.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Nao foi possivel localizar a raiz do projeto IndicA2.");
    }

    private static void ValidarNomeBanco(string databaseName)
    {
        if (!databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal) ||
            !databaseName[DatabasePrefix.Length..].All(char.IsAsciiLetterOrDigit))
        {
            throw new InvalidOperationException("A operacao destrutiva so e permitida para database temporario com prefixo indicaa2_test_.");
        }
    }
}

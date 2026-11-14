using Infrastructure.Database;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Trait("Category", "MySqlIntegration")]
public sealed class HomologationJournalIntegrationTests
{
    [Fact]
    public Task RejectsNonEmptySchemaWithoutJournal() => WithDatabase(async (factory, journal, _) =>
    {
        await using var connection = await OpenAsync(factory);
        await ExecuteAsync(connection, "CREATE TABLE legacy (id INT)");
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.ApplyAsync(connection, default));
        Assert.Equal(0, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='migration_journal'"));
    });

    [Fact]
    public Task ConcurrentMigratorsAndReapplicationAreIdempotent() => WithDatabase(async (factory, journal, _) =>
    {
        await Task.WhenAll(ApplyAsync(factory, journal), ApplyAsync(factory, journal));
        await using var connection = await OpenAsync(factory);
        Assert.True(await journal.IsReadyAsync(connection, default));
        var original = await TimestampAsync(connection);
        await ApplyAsync(factory, journal);
        Assert.Equal(original, await TimestampAsync(connection));
    });

    [Fact]
    public Task DivergentHashFailsClosed() => WithDatabase(async (factory, journal, _) =>
    {
        await ApplyAsync(factory, journal);
        await using var connection = await OpenAsync(factory);
        await ExecuteAsync(connection, "UPDATE migration_journal SET sha256=REPEAT('0',64) WHERE ordinal=2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.IsReadyAsync(connection, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.ApplyAsync(connection, default));
    });

    [Fact]
    public Task FailedIntermediateMigrationBlocksReapplication() => WithDatabase(async (factory, _, directory) =>
    {
        await File.AppendAllTextAsync(Path.Combine(directory, "004_add_codigo_indicacao_usuarios.sql"), "\nINVALID SQL;");
        var journal = new MigrationJournal(directory);
        await Assert.ThrowsAsync<MySqlException>(() => ApplyAsync(factory, journal));
        await using var connection = await OpenAsync(factory);
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM migration_journal WHERE applied_at IS NOT NULL"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM migration_journal WHERE applied_at IS NULL"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyAsync(factory, journal));
    });

    [Fact]
    public Task IncompleteJournalIsNotReady() => WithDatabase(async (factory, journal, _) =>
    {
        await ApplyAsync(factory, journal);
        await using var connection = await OpenAsync(factory);
        await ExecuteAsync(connection, "DELETE FROM migration_journal WHERE ordinal=16");
        Assert.False(await journal.IsReadyAsync(connection, default));
    });

    [Fact]
    public Task BootstrapIsConcurrentAndIdempotent() => WithDatabase(async (factory, journal, _) =>
    {
        await ApplyAsync(factory, journal);
        var password = Guid.NewGuid().ToString("N");
        await Task.WhenAll(BootstrapAsync(factory, journal, "admin@example.invalid", password),
            BootstrapAsync(factory, journal, "admin@example.invalid", password));
        await using var connection = await OpenAsync(factory);
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM usuarios"));
    });

    [Fact]
    public Task BootstrapRejectsConflictingAdministrator() => WithDatabase(async (factory, journal, _) =>
    {
        await ApplyAsync(factory, journal);
        var password = Guid.NewGuid().ToString("N");
        await BootstrapAsync(factory, journal, "admin@example.invalid", password);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BootstrapAsync(factory, journal, "other@example.invalid", password));
    });

    [Fact]
    public Task BootstrapDoesNotPromoteExistingUser() => WithDatabase(async (factory, journal, _) =>
    {
        await ApplyAsync(factory, journal);
        var repository = new Infrastructure.Repositories.UsuarioMySqlRepository(factory);
        var hasher = new Infrastructure.Security.BCryptPasswordHasher();
        var password = Guid.NewGuid().ToString("N");
        await repository.AdicionarAsync(new Domain.Entities.Usuario("Fictício", "user@example.invalid",
            hasher.HashPassword(password), codigoIndicacao: "ABCDEF12"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BootstrapAsync(factory, journal, "user@example.invalid", password));
        Assert.Equal(Domain.Enums.TipoUsuario.Usuario,
            (await repository.ObterPorEmailAsync("user@example.invalid"))!.TipoUsuario);
    });

    private static async Task WithDatabase(Func<MySqlConnectionFactory, MigrationJournal, string, Task> test)
    {
        var setting = Environment.GetEnvironmentVariable(MySqlIntegrationFixture.ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(setting))
            throw new InvalidOperationException("Conexão MySQL de integração obrigatória.");
        var connectionString = new MySqlConnectionStringBuilder(setting) { Pooling = false };
        if (connectionString.Database.Length != 0)
            throw new InvalidOperationException("Conexão de integração deve vir sem banco.");
        var preflight = await new MySqlIntegrationPreflight(new MySqlIntegrationConnectionProbe()).VerificarAsync(setting);
        Assert.True(preflight.Disponivel);
        var name = $"indicaa2_test_{Guid.NewGuid():N}";
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "IndicaA2.slnx")))
            root = root.Parent ?? throw new DirectoryNotFoundException();
        using var directory = new TempDirectory();
        foreach (var file in Directory.GetFiles(Path.Combine(root.FullName, "database"), "*.sql"))
            File.Copy(file, Path.Combine(directory.Path, Path.GetFileName(file)));

        await using var admin = new MySqlConnection(connectionString.ConnectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
        try
        {
            connectionString.Database = name;
            await test(new MySqlConnectionFactory(connectionString.ConnectionString),
                new MigrationJournal(directory.Path), directory.Path);
        }
        finally { await ExecuteAsync(admin, $"DROP DATABASE `{name}`"); }
    }

    private static async Task ApplyAsync(MySqlConnectionFactory factory, MigrationJournal journal)
    {
        await using var connection = await OpenAsync(factory);
        await journal.ApplyAsync(connection, default);
    }

    private static async Task BootstrapAsync(MySqlConnectionFactory factory, MigrationJournal journal, string email, string password)
    {
        await using var connection = await OpenAsync(factory);
        await HomologationBootstrap.CreateAsync(connection, journal, factory, "Fictício", email, password, default);
    }

    private static async Task<MySqlConnection> OpenAsync(MySqlConnectionFactory factory)
    {
        var connection = factory.Create();
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<DateTime> TimestampAsync(MySqlConnection connection)
    {
        await using var command = new MySqlCommand("SELECT MAX(applied_at) FROM migration_journal", connection);
        return (DateTime)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> ScalarAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("indicaa2-journal-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

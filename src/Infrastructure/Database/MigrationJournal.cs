using System.Security.Cryptography;
using MySqlConnector;

namespace Infrastructure.Database;

public sealed class MigrationJournal
{
    private static readonly string[] Names =
    [
        "002_create_usuarios.sql",
        "004_add_codigo_indicacao_usuarios.sql",
        "003_create_vistorias.sql",
        "001_create_indicacoes.sql",
        "005_create_pagamentos_vistoria.sql",
        "006_add_unicidade_vistoria_indicacoes.sql",
        "007_create_cashbacks.sql",
        "008_create_dados_pix.sql",
        "009_create_pagamentos_pix.sql",
        "010_create_operacoes_pagamento_pix.sql",
        "011_add_reconciliacao_lease_pagamentos_pix.sql",
        "012_add_envio_lease_pagamentos_pix.sql",
        "013_add_processamento_idx_pagamentos_pix.sql",
        "014_create_precificacao_vistorias.sql",
        "015_create_cobrancas_pix_vistoria.sql",
        "016_add_jornada_indicacao_notificacoes.sql"
    ];

    private readonly (string Name, byte[] Sql, string Hash)[] migrations;

    public MigrationJournal(string directory)
    {
        migrations = Names.Select(name =>
        {
            // Normalize checkout line endings so Windows and Linux agree on the journal.
            var sql = System.Text.Encoding.UTF8.GetBytes(
                File.ReadAllText(Path.Combine(directory, name)).Replace("\r\n", "\n"));
            return (name, sql, Convert.ToHexString(SHA256.HashData(sql)));
        }).ToArray();
    }

    public async Task<bool> IsReadyAsync(MySqlConnection connection, CancellationToken token)
        => await CompletedAsync(connection, token) == migrations.Length;

    public async Task ApplyAsync(MySqlConnection connection, CancellationToken token)
    {
        await AcquireLockAsync(connection, "migration", token);
        await using var journalExists = new MySqlCommand("""
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = 'migration_journal'
            """, connection);
        if (Convert.ToInt32(await journalExists.ExecuteScalarAsync(token)) == 0)
        {
            await using var objects = new MySqlCommand("""
                SELECT (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE())
                     + (SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema=DATABASE())
                     + (SELECT COUNT(*) FROM information_schema.events WHERE event_schema=DATABASE())
                """, connection);
            if (Convert.ToInt32(await objects.ExecuteScalarAsync(token)) != 0)
                throw new InvalidOperationException("Banco não vazio sem journal.");
            await ExecuteAsync(connection, """
                CREATE TABLE migration_journal (
                    ordinal INT PRIMARY KEY, name VARCHAR(128) NOT NULL UNIQUE,
                    sha256 CHAR(64) NOT NULL, applied_at DATETIME(6) NULL
                ) ENGINE=InnoDB
                """, token);
        }

        for (var i = await CompletedAsync(connection, token); i < migrations.Length; i++)
        {
            var (name, sql, hash) = migrations[i];
            // MySQL DDL commits implicitly. A failed or interrupted migration requires
            // operator recovery from a known backup before any retry.
            await using var mark = new MySqlCommand(
                "INSERT INTO migration_journal(ordinal,name,sha256) VALUES (@ordinal,@name,@hash)", connection);
            mark.Parameters.AddWithValue("@ordinal", i + 1);
            mark.Parameters.AddWithValue("@name", name);
            mark.Parameters.AddWithValue("@hash", hash);
            await mark.ExecuteNonQueryAsync(token);
            await ExecuteAsync(connection, System.Text.Encoding.UTF8.GetString(sql), token);
            await using var finish = new MySqlCommand(
                "UPDATE migration_journal SET applied_at=UTC_TIMESTAMP(6) WHERE ordinal=@ordinal", connection);
            finish.Parameters.AddWithValue("@ordinal", i + 1);
            await finish.ExecuteNonQueryAsync(token);
            Console.WriteLine($"Migration aplicada: {name}");
        }
    }

    internal static async Task AcquireLockAsync(MySqlConnection connection, string operation, CancellationToken token)
    {
        await using var command = new MySqlCommand("SELECT GET_LOCK(CONCAT(DATABASE(), ':', @operation), 15)", connection);
        command.Parameters.AddWithValue("@operation", operation);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1)
            throw new InvalidOperationException("Lock administrativo indisponível.");
        // The owning connection is non-pooled and is disposed by the caller.
    }

    private async Task<int> CompletedAsync(MySqlConnection connection, CancellationToken token)
    {
        await using var command = new MySqlCommand(
            "SELECT ordinal,name,sha256,applied_at FROM migration_journal ORDER BY ordinal", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var i = 0;
        while (await reader.ReadAsync(token))
        {
            if (i >= migrations.Length || reader.GetInt32(0) != i + 1
                || reader.GetString(1) != migrations[i].Name || reader.GetString(2) != migrations[i].Hash
                || reader.IsDBNull(3))
                throw new InvalidOperationException("Journal incompleto ou divergente.");
            i++;
        }
        return i;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }
}

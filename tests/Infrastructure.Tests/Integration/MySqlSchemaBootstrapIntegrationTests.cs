using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MySqlSchemaBootstrapIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task ScriptsReais_DevemCriarSchemaCompletoEmBancoNovo()
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        var tabelas = new List<string>();

        {
            await using var command = new MySqlCommand(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE();",
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                tabelas.Add(reader.GetString(0));
        }

        Assert.Contains("usuarios", tabelas);
        Assert.Contains("vistorias", tabelas);
        Assert.Contains("indicacoes", tabelas);
        Assert.Contains("pagamentos_vistoria", tabelas);

        var constraints = new List<string>();

        {
            await using var constraintCommand = new MySqlCommand(
                """
                SELECT constraint_name
                FROM information_schema.table_constraints
                WHERE table_schema = DATABASE()
                  AND table_name = 'indicacoes'
                  AND constraint_type = 'UNIQUE';
                """,
                connection);
            await using var constraintReader = await constraintCommand.ExecuteReaderAsync();

            while (await constraintReader.ReadAsync())
                constraints.Add(constraintReader.GetString(0));
        }

        Assert.Contains("uq_indicacoes_vistoria_id", constraints);
    }
}

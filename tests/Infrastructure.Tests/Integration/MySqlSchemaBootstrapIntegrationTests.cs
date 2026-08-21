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
        await using var command = new MySqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE();",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tabelas = new List<string>();

        while (await reader.ReadAsync())
            tabelas.Add(reader.GetString(0));

        Assert.Contains("usuarios", tabelas);
        Assert.Contains("vistorias", tabelas);
        Assert.Contains("indicacoes", tabelas);
        Assert.Contains("pagamentos_vistoria", tabelas);
    }
}

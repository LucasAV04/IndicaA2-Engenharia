using Application.Interfaces.Stores;
using Infrastructure.Repositories;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// Cobertura de schema e dos limites do seletor. Cenários que exigem dados
/// financeiros são executados somente pela suíte MySQL configurada.
/// </summary>
[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", MySqlIntegrationCategory.Name)]
public sealed class PagamentoPixCandidatoProcessamentoMySqlStoreIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task ObterCandidatosAsync_QuandoLimiteForInvalido_DeveRejeitarSemConsulta()
    {
        var store = CriarStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ObterCandidatosAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ObterCandidatosAsync(101));
    }

    [MySqlIntegrationFact]
    public async Task Migration013_DeveCriarIndiceSomenteComColunasEsperadas()
    {
        const string sql = """
            SELECT column_name
            FROM information_schema.statistics
            WHERE table_schema = DATABASE()
              AND table_name = 'pagamentos_pix'
              AND index_name = 'idx_pagamentos_pix_processamento'
            ORDER BY seq_in_index;
            """;
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var colunas = new List<string>();
        while (await reader.ReadAsync())
            colunas.Add(reader.GetString(0));

        Assert.Equal(new[] { "status", "updated_at", "id" }, colunas);
    }

    [MySqlIntegrationFact]
    public async Task ObterCandidatosAsync_QuandoNaoHouverPagamentos_DeveRetornarSomenteColecaoDeIds()
    {
        await fixture.LimparDadosAsync();
        IReadOnlyCollection<Guid> candidatos = await CriarStore().ObterCandidatosAsync(20);

        Assert.Empty(candidatos);
    }

    private IPagamentoPixCandidatoProcessamentoStore CriarStore() =>
        new PagamentoPixCandidatoProcessamentoMySqlStore(fixture.ConnectionFactory);
}

using Application.Interfaces.Stores;
using Domain.Enums;
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
            SELECT column_name, non_unique, table_name
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
        {
            colunas.Add(reader.GetString(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal("pagamentos_pix", reader.GetString(2));
        }

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

    [MySqlIntegrationFact]
    public async Task ObterCandidatosAsync_EstadosReaisSelecionaSomentePendenteEProcessandoSemMutacao()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        var pendente = await c.CriarAsync();
        var processando = await c.CriarAsync(StatusPagamentoPix.Processando, 1);
        await c.CriarAsync(StatusPagamentoPix.Falhou, 1);
        await c.CriarAsync(StatusPagamentoPix.Concluido, 1);
        await c.CriarAsync(StatusPagamentoPix.FalhaDefinitiva, 5);
        await c.CriarAsync(StatusPagamentoPix.Cancelado);
        var antes = await c.SnapshotIntegralAsync();
        var ids = await c.Seletor.ObterCandidatosAsync(100);
        Assert.Equal(new[] { pendente.Id, processando.Id }.Order(), ids.Order());
        Assert.Equal(antes, await c.SnapshotIntegralAsync());
    }

    [MySqlIntegrationFact]
    public Task ObterCandidatosAsync_LeaseEnvioValidoUsaRelogioMySqlENaoSeleciona() => VerificarLeaseAsync("envio", false);

    [MySqlIntegrationFact]
    public Task ObterCandidatosAsync_LeaseConsultaValidoUsaRelogioMySqlENaoSeleciona() => VerificarLeaseAsync("reconciliacao", false);

    [MySqlIntegrationFact]
    public Task ObterCandidatosAsync_LeaseEnvioExpiradoSelecionaSemLimpar() => VerificarLeaseAsync("envio", true);

    [MySqlIntegrationFact]
    public Task ObterCandidatosAsync_LeaseConsultaExpiradoSelecionaSemLimpar() => VerificarLeaseAsync("reconciliacao", true);

    private async Task VerificarLeaseAsync(string tipo, bool expirado)
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        var pix = await c.CriarAsync(StatusPagamentoPix.Processando, 1);
        await c.ExecutarAsync($"UPDATE pagamentos_pix SET {tipo}_lease_id = @token, " +
            $"{tipo}_lease_expira_em = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL {(expirado ? -10 : 10)} MINUTE) WHERE id = @id",
            ("@token", Guid.NewGuid().ToString()), ("@id", pix.Id.ToString()));
        var antes = await c.SnapshotIntegralAsync();
        var ids = await c.Seletor.ObterCandidatosAsync(20);
        if (expirado) Assert.Equal(pix.Id, Assert.Single(ids)); else Assert.Empty(ids);
        Assert.Equal(antes, await c.SnapshotIntegralAsync());
    }

    [MySqlIntegrationFact]
    public async Task ObterCandidatosAsync_LeasesParciaisSelecionaSemReparar()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        var esperados = new List<Guid>();
        foreach (var tipo in new[] { "envio", "reconciliacao" })
        foreach (var somenteToken in new[] { true, false })
        {
            var pix = await c.CriarAsync(StatusPagamentoPix.Processando, 1);
            esperados.Add(pix.Id);
            await c.ExecutarAsync(somenteToken
                ? $"UPDATE pagamentos_pix SET {tipo}_lease_id = @token WHERE id = @id"
                : $"UPDATE pagamentos_pix SET {tipo}_lease_expira_em = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 10 MINUTE) WHERE id = @id",
                ("@id", pix.Id.ToString()), ("@token", Guid.NewGuid().ToString()));
        }
        var antes = await c.SnapshotIntegralAsync();
        Assert.Equal(esperados.Order(), (await c.Seletor.ObterCandidatosAsync(100)).Order());
        Assert.Equal(antes, await c.SnapshotIntegralAsync());
    }

    [MySqlIntegrationFact]
    public async Task ObterCandidatosAsync_OrdenaPorUpdatedAtDepoisIdELimitaSemMutacao()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        var pagamentos = new List<Guid>();
        for (var i = 0; i < 4; i++) pagamentos.Add((await c.CriarAsync()).Id);
        var empate = pagamentos.Take(3).OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
        // Datas fixas de teste, sem sincronização baseada no relógio do processo.
        await c.ExecutarAsync("UPDATE pagamentos_pix SET updated_at = '2030-01-02 00:00:00.000000'");
        await c.ExecutarAsync("UPDATE pagamentos_pix SET updated_at = '2030-01-01 00:00:00.000000' WHERE id = @id",
            ("@id", pagamentos[3].ToString()));
        var antes = await c.SnapshotIntegralAsync();
        var esperado = new[] { pagamentos[3], empate[0], empate[1] };
        Assert.Equal(esperado, await c.Seletor.ObterCandidatosAsync(3));
        Assert.Equal(esperado, await c.Seletor.ObterCandidatosAsync(3));
        Assert.Equal(antes, await c.SnapshotIntegralAsync());
    }

    [MySqlIntegrationFact]
    public async Task ObterCandidatosAsync_PreservaAuditoriaSnapshotsEMaterialCriptografico()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        var pix = await c.CriarAsync();
        await c.Envios.TentarPrepararEnvioAsync(pix.Id);
        await c.ExpirarEnvioAsync(pix.Id);
        var antes = await c.SnapshotIntegralAsync();
        Assert.Equal(pix.Id, Assert.Single(await c.Seletor.ObterCandidatosAsync(1)));
        Assert.Equal(antes, await c.SnapshotIntegralAsync());
    }

    [MySqlIntegrationFact]
    public async Task Migration013_AlteraSomenteIndiceEPreservaSchemaFinanceiroEDados()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        await c.CriarAsync();
        var dados = await c.SnapshotIntegralAsync();
        const string schema = """
            SELECT table_name, column_name, ordinal_position, column_type, is_nullable, column_default, extra
            FROM information_schema.columns WHERE table_schema = DATABASE() ORDER BY table_name, ordinal_position
            """;
        const string constraints = """
            SELECT table_name, constraint_name, constraint_type FROM information_schema.table_constraints
            WHERE constraint_schema = DATABASE() ORDER BY table_name, constraint_name
            """;
        var colunas = await c.SnapshotAsync(schema);
        var restricoes = await c.SnapshotAsync(constraints);
        var indices = await c.SnapshotAsync("""
            SELECT table_name, index_name, non_unique, seq_in_index, column_name FROM information_schema.statistics
            WHERE table_schema = DATABASE() ORDER BY table_name, index_name, seq_in_index
            """);
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "IndicaA2.slnx"))) raiz = raiz.Parent;
        Assert.NotNull(raiz);
        var migration = await File.ReadAllTextAsync(Path.Combine(raiz!.FullName, "database", "013_add_processamento_idx_pagamentos_pix.sql"));
        await c.ExecutarAsync("DROP INDEX idx_pagamentos_pix_processamento ON pagamentos_pix");
        var restaurado = false;
        try
        {
            await c.ExecutarAsync(migration);
            restaurado = true;
            Assert.Equal(colunas, await c.SnapshotAsync(schema));
            Assert.Equal(restricoes, await c.SnapshotAsync(constraints));
            Assert.Equal(indices, await c.SnapshotAsync("""
                SELECT table_name, index_name, non_unique, seq_in_index, column_name FROM information_schema.statistics
                WHERE table_schema = DATABASE() ORDER BY table_name, index_name, seq_in_index
                """));
            Assert.Equal(dados, await c.SnapshotIntegralAsync());
        }
        finally
        {
            if (!restaurado) await c.ExecutarAsync(migration);
        }
    }
}

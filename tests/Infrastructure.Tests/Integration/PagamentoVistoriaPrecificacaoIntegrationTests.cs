using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Infrastructure.Repositories;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", MySqlIntegrationCategory.Name)]
public sealed class PagamentoVistoriaPrecificacaoIntegrationTests(MySqlIntegrationFixture fixture)
{
    private static readonly DateTime Instante = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private PrecificacaoMySqlStore Precos => new(fixture.ConnectionFactory);
    private PagamentoVistoriaMySqlRepository Pagamentos => new(fixture.ConnectionFactory);
    private VistoriaMySqlRepository Vistorias => new(fixture.ConnectionFactory);
    private PagamentoVistoriaService Service(IPagamentoVistoriaRepository? repo = null) => new(repo ?? Pagamentos, Vistorias);

    private async Task<(TipoPlanta Tipo, Vistoria Vistoria)> Preparar(bool legado = false)
    {
        await fixture.LimparDadosAsync();
        var usuario = IntegrationTestData.CriarUsuario();
        await new UsuarioMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(usuario);
        var tipo = new TipoPlanta("Tipo fictício", DateTime.UtcNow);
        await Precos.CriarTipoAsync(tipo, default);
        await Precos.PublicarAsync(tipo.Id, new(1.2345m, ModalidadeAcrescimo.Fixo, 0, 0), DateTime.UtcNow, default);
        var vistoria = legado ? IntegrationTestData.CriarVistoria(usuario.Id)
            : await Precos.CriarVistoriaAsync(usuario.Id, tipo.Id, 10, PacoteVistoria.Simples, Instante, Instante, default);
        if (legado) await Vistorias.AdicionarAsync(vistoria);
        return (tipo, vistoria);
    }

    private async Task<string> Snapshot()
    {
        using var c = new ProcessamentoPixCenario(fixture);
        return string.Join("|", await c.SnapshotAsync("SELECT * FROM tipos_planta ORDER BY id"),
            await c.SnapshotAsync("SELECT * FROM precos_vistoria ORDER BY id"), await c.SnapshotAsync("SELECT * FROM vistorias ORDER BY id"),
            await c.SnapshotAsync("SELECT * FROM cashbacks ORDER BY id"), await c.SnapshotAsync("SELECT * FROM pagamentos_pix ORDER BY id"),
            await c.SnapshotAsync("SELECT * FROM operacoes_pagamento_pix ORDER BY id"));
    }

    [MySqlIntegrationFact]
    public async Task PagamentoDerivaSnapshotComDuasCasasSemAlterarOutrasTabelas()
    {
        var (_, v) = await Preparar(); var antes = await Snapshot();
        var dto = await Service().CriarAsync(new() { VistoriaId = v.Id });
        var pagamento = await Pagamentos.ObterPorIdAsync(dto.Id);
        Assert.Equal(v.Precificacao!.ValorFinal, pagamento!.Valor);
        Assert.Equal(12.35m, pagamento.Valor);
        Assert.Equal(2, (decimal.GetBits(pagamento.Valor)[3] >> 16) & 0xff);
        Assert.Equal(antes, await Snapshot());
        Assert.Single(await Pagamentos.ObterTodosAsync());
        Assert.Equal(StatusPagamentoVistoria.Pendente, pagamento.Status);
    }

    [MySqlIntegrationFact]
    public async Task PublicacaoRenomeacaoDesativacaoNaoAlteramValorHistoricoDoPagamento()
    {
        var (tipo, v) = await Preparar(); var snapshot = v.Precificacao;
        await Precos.RenomearTipoAsync(tipo.Id, "Renomeado fictício", DateTime.UtcNow, default);
        var novo = await Precos.PublicarAsync(tipo.Id, new(900, ModalidadeAcrescimo.Percentual, 50, 1), DateTime.UtcNow, default);
        await Precos.DesativarPrecoAsync(tipo.Id, novo.Id, DateTime.UtcNow, default);
        await Precos.DesativarTipoAsync(tipo.Id, DateTime.UtcNow, default);
        var antes = await Snapshot();
        var pagamento = await Service().CriarAsync(new() { VistoriaId = v.Id });
        Assert.Equal(12.35m, pagamento.Valor);
        Assert.Equal(snapshot, (await Vistorias.ObterPorIdAsync(v.Id))!.Precificacao);
        Assert.Equal(antes, await Snapshot());
    }

    [MySqlIntegrationFact]
    public async Task LegadoComReferenciasNulasNaoCriaPagamentoNemRecalcula()
    {
        var (_, v) = await Preparar(true); var antes = await Snapshot();
        await Assert.ThrowsAsync<VistoriaLegadaSemPrecificacaoException>(() => Service().CriarAsync(new() { VistoriaId = v.Id }));
        Assert.Empty(await Pagamentos.ObterTodosAsync());
        Assert.Null((await Vistorias.ObterPorIdAsync(v.Id))!.Precificacao);
        Assert.Equal(antes, await Snapshot());
    }

    [MySqlIntegrationFact]
    public async Task CancelamentoNaPersistenciaDeixaZeroPagamento()
    {
        var (_, v) = await Preparar(); var antes = await Snapshot(); using var cts = new CancellationTokenSource();
        var repo = new RepositorioCoordenado(Pagamentos, () => { cts.Cancel(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(repo).CriarAsync(new() { VistoriaId = v.Id }, cts.Token));
        Assert.Empty(await Pagamentos.ObterTodosAsync()); Assert.Equal(antes, await Snapshot());
    }

    [MySqlIntegrationFact]
    public async Task DuasCriacoesConcorrentesPersistemUmPagamentoDoMesmoSnapshot()
    {
        var (_, v) = await Preparar(); var antes = await Snapshot();
        var chegaram = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var quantidade = 0;
        var repo = new RepositorioCoordenado(Pagamentos, async () =>
        {
            if (Interlocked.Increment(ref quantidade) == 2) chegaram.TrySetResult();
            await liberar.Task.WaitAsync(TimeSpan.FromSeconds(10));
        });
        async Task<bool> Criar()
        {
            try { await Service(repo).CriarAsync(new() { VistoriaId = v.Id }); return true; }
            catch (PagamentoVistoriaDuplicadoException) { return false; }
        }
        var a = Criar(); var b = Criar();
        try
        {
            var primeiro = await Task.WhenAny(chegaram.Task, a, b).WaitAsync(TimeSpan.FromSeconds(10));
            if (primeiro != chegaram.Task) { await primeiro; Assert.Fail("Executor terminou antes da barreira de inserção."); }
        }
        finally { liberar.TrySetResult(); await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Single(new[] { await a, await b }, x => x);
        Assert.Equal(12.35m, Assert.Single(await Pagamentos.ObterTodosAsync()).Valor);
        Assert.Equal(antes, await Snapshot());
    }

    [MySqlIntegrationFact]
    public async Task FkCompostaRejeitaPrecoDeOutroTipoSemMutacao()
    {
        var (_, v) = await Preparar();
        var outro = new TipoPlanta("Outro fictício", DateTime.UtcNow); await Precos.CriarTipoAsync(outro, default);
        var antes = await Snapshot();
        await RejeitarAtualizacao(v.Id, "tipo_planta_id", outro.Id.ToString());
        Assert.Equal(antes, await Snapshot());
    }

    [MySqlIntegrationFact]
    public async Task FkCompostaRejeitaVersaoIncompativelSemMutacao()
    {
        var (_, v) = await Preparar(); var antes = await Snapshot();
        await RejeitarAtualizacao(v.Id, "preco_versao", 2);
        Assert.Equal(antes, await Snapshot());
    }

    private async Task RejeitarAtualizacao(Guid id, string coluna, object valor)
    {
        Assert.Contains(coluna, new[] { "tipo_planta_id", "preco_versao" });
        await using var c = fixture.ConnectionFactory.Create(); await c.OpenAsync();
        await using var cmd = new MySqlCommand($"UPDATE vistorias SET {coluna}=@valor WHERE id=@id;", c);
        cmd.Parameters.AddWithValue("@valor", valor); cmd.Parameters.AddWithValue("@id", id.ToString());
        var ex = await Assert.ThrowsAsync<MySqlException>(() => cmd.ExecuteNonQueryAsync()); Assert.Equal(1452, ex.Number);
    }

    [MySqlIntegrationFact]
    public async Task SchemaConfirmaIdentidadeCompostaRestritivaEIndiceUnico()
    {
        await using var c = fixture.ConnectionFactory.Create(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT column_name,referenced_table_name,referenced_column_name FROM information_schema.key_column_usage WHERE constraint_schema=DATABASE() AND constraint_name='fk_vistorias_preco' ORDER BY ordinal_position;", c);
        var colunas = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync()) while (await r.ReadAsync()) colunas.Add($"{r.GetString(0)}:{r.GetString(1)}:{r.GetString(2)}");
        Assert.Equal(new[] { "preco_vistoria_id:precos_vistoria:id", "tipo_planta_id:precos_vistoria:tipo_planta_id", "preco_versao:precos_vistoria:versao" }, colunas);
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.referential_constraints WHERE constraint_schema=DATABASE() AND constraint_name='fk_vistorias_preco' AND update_rule IN ('RESTRICT','NO ACTION') AND delete_rule IN ('RESTRICT','NO ACTION');";
        Assert.Equal(1L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
        cmd.CommandText = "SELECT column_name FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='precos_vistoria' AND index_name='uq_precos_identidade' AND non_unique=0 ORDER BY seq_in_index;";
        var indice = new List<string>(); await using (var r = await cmd.ExecuteReaderAsync()) while (await r.ReadAsync()) indice.Add(r.GetString(0));
        Assert.Equal(new[] { "id", "tipo_planta_id", "versao" }, indice);
    }

    private sealed class RepositorioCoordenado(IPagamentoVistoriaRepository real, Func<Task> antesInsert) : IPagamentoVistoriaRepository
    {
        public Task<PagamentoVistoria?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default) => real.ObterPorIdAsync(id, cancellationToken);
        public Task<PagamentoVistoria?> ObterPorVistoriaIdAsync(Guid id, CancellationToken cancellationToken = default) => real.ObterPorVistoriaIdAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<PagamentoVistoria>> ObterTodosAsync(CancellationToken cancellationToken = default) => real.ObterTodosAsync(cancellationToken);
        public async Task AdicionarAsync(PagamentoVistoria p, CancellationToken cancellationToken = default) { await antesInsert(); await real.AdicionarAsync(p, cancellationToken); }
        public Task AtualizarAsync(PagamentoVistoria p, CancellationToken cancellationToken = default) => real.AtualizarAsync(p, cancellationToken);
    }
}

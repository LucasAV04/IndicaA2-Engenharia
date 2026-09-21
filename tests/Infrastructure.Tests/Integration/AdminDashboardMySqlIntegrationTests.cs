using System.Text.Json;
using Application.Mapping;
using Domain.Enums;
using Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", MySqlIntegrationCategory.Name)]
public sealed class AdminDashboardMySqlIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task DashboardVazioRetornaTodosOsTotaisZero()
    {
        await fixture.LimparDadosAsync();
        var result = await new AdminDashboardMySqlStore(fixture.ConnectionFactory).ObterAsync();
        Assert.Equal(0, result.TotalUsuarios);
        Assert.Equal(0, result.UsuariosAtivos);
        Assert.All(new[] { result.Indicacoes, result.Vistorias, result.PagamentosVistoria, result.Cashbacks, result.PagamentosPix },
            counts => { Assert.NotEmpty(counts); Assert.All(counts.Values, count => Assert.Equal(0, count)); });
        Assert.All(new[] { result.ReceitaConfirmada, result.PagamentosPendentes, result.CashbackDisponivel, result.CashbackPago, result.PixPendenteProcessando, result.PixConcluido },
            value => Assert.Equal(0m, value));
        Assert.Equal(DateTimeKind.Utc, result.CalculadoEmUtc.Kind);
    }

    [MySqlIntegrationFact]
    public async Task DashboardSomaDecimaisEAgrupaEstadosSemDuplicarJoins()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        foreach (var (status, tentativas) in new[] {
            (StatusPagamentoPix.Pendente, 0), (StatusPagamentoPix.Processando, 1),
            (StatusPagamentoPix.Concluido, 1), (StatusPagamentoPix.Falhou, 1),
            (StatusPagamentoPix.FalhaDefinitiva, 5), (StatusPagamentoPix.Cancelado, 0) })
        {
            var pix = await c.CriarAsync(status, tentativas);
            if (status == StatusPagamentoPix.Concluido)
                await c.ExecutarAsync("UPDATE cashbacks SET status=2 WHERE id=@id", ("@id", pix.CashbackId.ToString()));
        }
        await c.ExecutarAsync("UPDATE pagamentos_vistoria SET status=0, pago_em=NULL, valor=12.34 ORDER BY id LIMIT 1");
        await c.ExecutarAsync("UPDATE usuarios SET status=2 ORDER BY id LIMIT 1");
        var result = await new AdminDashboardMySqlStore(fixture.ConnectionFactory).ObterAsync();
        Assert.Equal(12, result.TotalUsuarios);
        Assert.Equal(11, result.UsuariosAtivos);
        Assert.Equal(6, result.Indicacoes["VistoriaVinculada"]);
        Assert.Equal(6, result.Vistorias["Agendada"]);
        Assert.Equal(5, result.PagamentosVistoria["Confirmado"]);
        Assert.Equal(1, result.PagamentosVistoria["Pendente"]);
        Assert.Equal(2499.50m, result.ReceitaConfirmada);
        Assert.Equal(12.34m, result.PagamentosPendentes);
        Assert.Equal(499.90m, result.CashbackDisponivel);
        Assert.Equal(99.98m, result.CashbackPago);
        Assert.Equal(199.96m, result.PixPendenteProcessando);
        Assert.Equal(99.98m, result.PixConcluido);
        Assert.All(result.PagamentosPix.Values, count => Assert.Equal(1, count));
        Assert.Equal(1, result.FalhasPix);
        Assert.Equal(1, result.FalhasDefinitivasPix);
    }

    [MySqlIntegrationFact]
    public async Task DashboardPreservaTodasAsTabelasENaoLeOuDescriptografaMaterialPix()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        await c.CriarAsync();
        // O agregado deve funcionar mesmo com ciphertext ilegível: não materializa chaves.
        await c.ExecutarAsync("UPDATE pagamentos_pix SET chave_pix_ciphertext=X'00'");
        var before = await Snapshot();
        var result = await new AdminDashboardMySqlStore(fixture.ConnectionFactory).ObterAsync();
        Assert.Equal(before, await Snapshot());
        var json = JsonSerializer.Serialize(result);
        foreach (var forbidden in new[] { "ChavePix", "ciphertext", "nonce", "tag", "Lease", "Provider", "ficticio@" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);

        async Task<string> Snapshot() => string.Join("\n",
            await c.SnapshotIntegralAsync(), await c.SnapshotAsync("SELECT * FROM usuarios ORDER BY id"),
            await c.SnapshotAsync("SELECT * FROM indicacoes ORDER BY id"), await c.SnapshotAsync("SELECT * FROM vistorias ORDER BY id"),
            await c.SnapshotAsync("SELECT * FROM pagamentos_vistoria ORDER BY id"), await c.SnapshotAsync("SELECT * FROM dados_pix ORDER BY id"));
    }

    [MySqlIntegrationFact]
    public async Task ListagemPixOrdenaCreatedAtEIdDescPreservaSnapshotERespostaSegura()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        var first = await c.CriarAsync();
        var second = await c.CriarAsync();
        var third = await c.CriarAsync();
        await c.ExecutarAsync("UPDATE pagamentos_pix SET created_at='2026-01-01 00:00:00', updated_at='2026-02-01 00:00:00'");
        await c.ExecutarAsync("UPDATE pagamentos_pix SET created_at='2026-01-02 00:00:00' WHERE id=@id", ("@id", third.Id.ToString()));
        var before = await c.SnapshotIntegralAsync();
        var rows = await c.Pagamentos.ObterTodosAsync();
        var expected = new[] { third.Id }.Concat(new[] { first.Id, second.Id }.OrderByDescending(id => id.ToString(), StringComparer.Ordinal)).ToArray();
        Assert.Equal(expected, rows.Select(r => r.Id));
        var dto = rows.ToResponseDto();
        Assert.Equal(expected, dto.Select(r => r.Id));
        Assert.DoesNotContain("ficticio@example.invalid", JsonSerializer.Serialize(dto));
        Assert.Equal(before, await c.SnapshotIntegralAsync());
    }

    [MySqlIntegrationFact]
    public async Task ConsultasNovasRespeitamCancelamentoSemMutacao()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        await c.CriarAsync();
        var before = await c.SnapshotIntegralAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AdminDashboardMySqlStore(fixture.ConnectionFactory).ObterAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.Pagamentos.ObterTodosAsync(cancellation.Token));
        Assert.Equal(before, await c.SnapshotIntegralAsync());
    }
}

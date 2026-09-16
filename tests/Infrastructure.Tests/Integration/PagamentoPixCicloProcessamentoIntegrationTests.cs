using System.Collections.Concurrent;
using Application.Interfaces.Providers;
using Application.Models;
using Domain.Enums;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", MySqlIntegrationCategory.Name)]
public sealed class PagamentoPixCicloProcessamentoIntegrationTests(MySqlIntegrationFixture fixture)
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(10);

    [MySqlIntegrationFact]
    public Task Ciclo_RecuperacaoConfirmadaLiquidaPixECashbackUmaVez() =>
        VerificarRecuperacaoAsync(PixProviderResult.Confirmado("ficticio", "OK"), StatusPagamentoPix.Concluido, StatusCashback.Pago);

    [MySqlIntegrationFact]
    public Task Ciclo_RecuperacaoComFalhaConfirmadaNaoPagaNemIniciaRetry() =>
        VerificarRecuperacaoAsync(PixProviderResult.FalhaConfirmada("ficticio", "FALHA"), StatusPagamentoPix.Falhou, StatusCashback.Disponivel);

    [MySqlIntegrationFact]
    public Task Ciclo_RecuperacaoQuintaFalhaTornaDefinitivaSemRetry() =>
        VerificarRecuperacaoAsync(PixProviderResult.FalhaConfirmada(), StatusPagamentoPix.FalhaDefinitiva, StatusCashback.Disponivel, 5);

    [MySqlIntegrationFact]
    public Task Ciclo_RecuperacaoPendenteMantemEvidenciaSemConsultaImediata() =>
        VerificarRecuperacaoAsync(PixProviderResult.Pendente(), StatusPagamentoPix.Processando, StatusCashback.Disponivel);

    [MySqlIntegrationFact]
    public Task Ciclo_RecuperacaoIndeterminadaMantemEvidenciaSemConsultaImediata() =>
        VerificarRecuperacaoAsync(PixProviderResult.Indeterminado(), StatusPagamentoPix.Processando, StatusCashback.Disponivel);

    private async Task VerificarRecuperacaoAsync(PixProviderResult resposta, StatusPagamentoPix status,
        StatusCashback statusCashback, int tentativa = 1)
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        var pix = tentativa == 1 ? await c.CriarAsync() : await c.CriarAsync(StatusPagamentoPix.Falhou, tentativa - 1);
        var inicial = await c.Envios.TentarPrepararEnvioAsync(pix.Id);
        Assert.True(inicial.Adquirido);
        await c.ExpirarEnvioAsync(pix.Id);
        var imutavel = await c.SnapshotImutavelAsync(pix.Id);
        var cashbackAntes = await c.SnapshotAsync("SELECT * FROM cashbacks ORDER BY id");
        var opAntes = Assert.Single(await c.Operacoes.ObterPorPagamentoPixIdAsync(pix.Id));
        Assert.Equal(pix.Id, Assert.Single(await c.Seletor.ObterCandidatosAsync(20)));
        Guid? novoToken = null;
        var provider = new ProviderFalso(resposta)
        {
            AntesDaResposta = async request =>
            {
                novoToken = await c.TokenEnvioAsync(pix.Id);
                Assert.NotNull(novoToken);
                Assert.NotEqual(inicial.LeaseId, novoToken);
                Assert.Equal(inicial.ReferenciaIdempotente, request.ReferenciaIdempotente);
                Assert.Equal(pix.Id, request.PagamentoPixId);
                Assert.Equal(pix.Valor, request.Valor);
                Assert.Equal(pix.ChavePix, request.ChavePix);
                var op = Assert.Single(await c.Operacoes.ObterPorPagamentoPixIdAsync(pix.Id));
                Assert.Equal(opAntes.Id, op.Id);
                Assert.Equal(tentativa, op.NumeroTentativaEnvio);
                Assert.Null(op.FinishedAt);
                // O executor antigo é rejeitado ainda enquanto o novo lease está ativo.
                var snapshot = await c.SnapshotIntegralAsync();
                Assert.False((await c.Envios.FinalizarEnvioAsync(pix.Id, op.Id, inicial.LeaseId!.Value,
                    ResultadoOperacaoPagamentoPix.Confirmado, "antigo", "antigo")).Finalizada);
                Assert.Equal(snapshot, await c.SnapshotIntegralAsync());
            }
        };
        var resultado = await c.Processador(provider).ProcessarAsync(pix.Id);
        Assert.Equal(status == StatusPagamentoPix.Processando
            ? StatusProcessamentoPagamentoPix.EnvioExecutadoAguardandoResultado
            : StatusProcessamentoPagamentoPix.Aplicado, resultado.Status);
        Assert.Equal(pix.Id, resultado.PagamentoPixId);
        Assert.Equal(1, provider.Envios);
        Assert.Equal(0, provider.Consultas);
        Assert.Single(provider.Efeitos);
        Assert.Equal(inicial.ReferenciaIdempotente, Assert.Single(provider.Referencias));
        var persistido = (await c.Pagamentos.ObterPorIdAsync(pix.Id))!;
        Assert.Equal(status, persistido.Status);
        Assert.Equal(tentativa, persistido.QuantidadeTentativas);
        Assert.Equal(statusCashback, (await c.Cashbacks.ObterPorIdAsync(pix.CashbackId))!.Status);
        var opFinal = Assert.Single(await c.Operacoes.ObterPorPagamentoPixIdAsync(pix.Id));
        Assert.Equal(opAntes.Id, opFinal.Id);
        Assert.Equal(opAntes.CreatedAt, opFinal.CreatedAt);
        Assert.Equal(opAntes.ReferenciaIdempotente, opFinal.ReferenciaIdempotente);
        Assert.Equal(tentativa, opFinal.NumeroTentativaEnvio);
        Assert.NotNull(opFinal.FinishedAt);
        Assert.Equal(resposta.Status.ToString(), opFinal.Resultado.ToString());
        Assert.Equal(resposta.IdentificadorProvider, opFinal.IdentificadorProvider);
        Assert.Equal(resposta.Codigo, opFinal.Codigo);
        Assert.Null(await c.TokenEnvioAsync(pix.Id));
        Assert.Equal(imutavel, await c.SnapshotImutavelAsync(pix.Id));
        Assert.DoesNotContain(pix.ChavePix, resultado.ToString());
        if (statusCashback == StatusCashback.Disponivel)
            Assert.Equal(cashbackAntes, await c.SnapshotAsync("SELECT * FROM cashbacks ORDER BY id"));
        if (status != StatusPagamentoPix.Processando)
        {
            var final = await c.SnapshotIntegralAsync();
            await c.Processador(provider).ProcessarAsync(pix.Id);
            Assert.Equal(final, await c.SnapshotIntegralAsync());
            Assert.Equal(1, provider.Envios);
            Assert.Equal(0, provider.Consultas);
        }
    }

    [MySqlIntegrationFact]
    public async Task CiclosConcorrentes_MesmoCandidatoTemUmEnvioUmaAuditoriaEUmPagamento()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        var pix = await c.CriarAsync();
        var imutavel = await c.SnapshotImutavelAsync(pix.Id);
        var ambosSelecionaram = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primeiroNoProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selecoes = 0;
        var provider = new ProviderFalso(PixProviderResult.Confirmado("ficticio", "OK"))
        {
            AntesDaResposta = async _ =>
            {
                primeiroNoProvider.TrySetResult();
                await liberar.Task.WaitAsync(Limite);
            }
        };
        async Task<ResultadoProcessamentoPagamentoPix> CicloAsync(bool primeiro)
        {
            // Cada ciclo possui seletor/orquestrador reais; ambos recebem o candidato antes do claim.
            var id = Assert.Single(await c.Seletor.ObterCandidatosAsync(20));
            Assert.Equal(pix.Id, id);
            if (Interlocked.Increment(ref selecoes) == 2) ambosSelecionaram.TrySetResult();
            await ambosSelecionaram.Task.WaitAsync(Limite);
            if (!primeiro) await primeiroNoProvider.Task.WaitAsync(Limite);
            return await c.Processador(provider).ProcessarAsync(id);
        }
        var a = CicloAsync(true);
        var b = CicloAsync(false);
        try
        {
            await Task.WhenAny(primeiroNoProvider.Task, a).WaitAsync(Limite);
            if (a.IsCompleted) await a; // Propaga falha antes do sinal.
            var segundo = await b.WaitAsync(Limite);
            Assert.Equal(StatusProcessamentoPagamentoPix.EnvioEmAndamento, segundo.Status);
            Assert.Equal(1, provider.Envios);
            Assert.Equal(StatusCashback.Disponivel, (await c.Cashbacks.ObterPorIdAsync(pix.CashbackId))!.Status);
            var operacao = Assert.Single(await c.Operacoes.ObterPorPagamentoPixIdAsync(pix.Id));
            Assert.Null(operacao.FinishedAt);
            Assert.Equal(1, operacao.NumeroTentativaEnvio);
            Assert.NotNull(await c.TokenEnvioAsync(pix.Id));
        }
        finally
        {
            liberar.TrySetResult();
            await Task.WhenAll(a, b).WaitAsync(Limite);
        }
        Assert.Equal(StatusProcessamentoPagamentoPix.Aplicado, (await a).Status);
        Assert.Equal(1, provider.Envios);
        Assert.Equal(0, provider.Consultas);
        Assert.Single(provider.Efeitos);
        Assert.Equal(pix.Id.ToString("N"), Assert.Single(provider.Referencias));
        var op = Assert.Single(await c.Operacoes.ObterPorPagamentoPixIdAsync(pix.Id));
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, op.Resultado);
        Assert.NotNull(op.FinishedAt);
        Assert.Equal(1, (await c.Pagamentos.ObterPorIdAsync(pix.Id))!.QuantidadeTentativas);
        Assert.Equal(StatusPagamentoPix.Concluido, (await c.Pagamentos.ObterPorIdAsync(pix.Id))!.Status);
        Assert.Equal(StatusCashback.Pago, (await c.Cashbacks.ObterPorIdAsync(pix.CashbackId))!.Status);
        Assert.Null(await c.TokenEnvioAsync(pix.Id));
        Assert.Equal(imutavel, await c.SnapshotImutavelAsync(pix.Id));
        var final = await c.SnapshotIntegralAsync();
        Assert.Equal(StatusProcessamentoPagamentoPix.Terminal, (await c.Processador(provider).ProcessarAsync(pix.Id)).Status);
        Assert.Equal(final, await c.SnapshotIntegralAsync());
        Assert.Equal(1, provider.Envios);
    }

    [MySqlIntegrationFact]
    public async Task Ciclo_PendenteComLeaseIncompativelRecusaSemProviderNemReparacao()
    {
        // Completo: recusa a aquisição; parcial/simultâneo: inconsistência explícita.
        foreach (var modo in new[] { "envio", "reconciliacao", "parcial", "simultaneo" })
        {
            await fixture.LimparDadosAsync();
            using var c = new ProcessamentoPixCenario(fixture);
            var pix = await c.CriarAsync();
            var sql = modo switch
            {
                "envio" => "envio_lease_id=@token, envio_lease_expira_em=DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 10 MINUTE)",
                "reconciliacao" => "reconciliacao_lease_id=@token, reconciliacao_lease_expira_em=DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 10 MINUTE)",
                "parcial" => "envio_lease_id=@token",
                _ => "envio_lease_id=@token, envio_lease_expira_em=UTC_TIMESTAMP(6), reconciliacao_lease_id=@token, reconciliacao_lease_expira_em=UTC_TIMESTAMP(6)"
            };
            await c.ExecutarAsync($"UPDATE pagamentos_pix SET {sql} WHERE id=@id", ("@token", Guid.NewGuid().ToString()), ("@id", pix.Id.ToString()));
            var antes = await c.SnapshotIntegralAsync();
            Assert.Equal(pix.Id, Assert.Single(await c.Seletor.ObterCandidatosAsync(20)));
            var provider = new ProviderFalso(PixProviderResult.Confirmado());
            if (modo is "parcial" or "simultaneo")
                await Assert.ThrowsAsync<InvalidOperationException>(() => c.Processador(provider).ProcessarAsync(pix.Id));
            else
                Assert.Equal(StatusProcessamentoPagamentoPix.EstadoAlteradoConcorrentemente,
                    (await c.Processador(provider).ProcessarAsync(pix.Id)).Status);
            Assert.Equal(0, provider.Envios);
            Assert.Equal(0, provider.Consultas);
            Assert.Equal(antes, await c.SnapshotIntegralAsync());
        }
    }

    [MySqlIntegrationFact]
    public async Task Ciclo_RecuperacaoConfirmadaComFalhaNoCashbackRevertePagamentoSemReenvio()
    {
        await fixture.LimparDadosAsync();
        using var c = new ProcessamentoPixCenario(fixture);
        var pix = await c.CriarAsync();
        await c.Envios.TentarPrepararEnvioAsync(pix.Id);
        await c.ExpirarEnvioAsync(pix.Id);
        var imutavel = await c.SnapshotImutavelAsync(pix.Id);
        var cashbackAntes = await c.SnapshotAsync("SELECT * FROM cashbacks ORDER BY id");
        var trigger = $"teste_cashback_{Guid.NewGuid():N}";
        await c.ExecutarAsync($"CREATE TRIGGER {trigger} BEFORE UPDATE ON cashbacks FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'falha ficticia controlada'");
        var provider = new ProviderFalso(PixProviderResult.Confirmado());
        try
        {
            await Assert.ThrowsAsync<MySqlException>(() => c.Processador(provider).ProcessarAsync(pix.Id));
            Assert.Equal(StatusPagamentoPix.Processando, (await c.Pagamentos.ObterPorIdAsync(pix.Id))!.Status);
            Assert.Equal(cashbackAntes, await c.SnapshotAsync("SELECT * FROM cashbacks ORDER BY id"));
            Assert.Equal(imutavel, await c.SnapshotImutavelAsync(pix.Id));
            var op = Assert.Single(await c.Operacoes.ObterPorPagamentoPixIdAsync(pix.Id));
            Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, op.Resultado);
            Assert.NotNull(op.FinishedAt);
            Assert.Null(await c.TokenEnvioAsync(pix.Id));
        }
        finally { await c.ExecutarAsync($"DROP TRIGGER IF EXISTS {trigger}"); }
        Assert.Equal(StatusProcessamentoPagamentoPix.Aplicado, (await c.Processador(provider).ProcessarAsync(pix.Id)).Status);
        Assert.Equal(StatusCashback.Pago, (await c.Cashbacks.ObterPorIdAsync(pix.CashbackId))!.Status);
        Assert.Equal(StatusPagamentoPix.Concluido, (await c.Pagamentos.ObterPorIdAsync(pix.Id))!.Status);
        Assert.Equal(1, provider.Envios);
        Assert.Equal(0, provider.Consultas);
    }

    private sealed class ProviderFalso(PixProviderResult resposta) : IPixProvider
    {
        private int _envios;
        private int _consultas;
        public int Envios => Volatile.Read(ref _envios);
        public int Consultas => Volatile.Read(ref _consultas);
        public ConcurrentDictionary<string, byte> Efeitos { get; } = new();
        public ConcurrentQueue<string> Referencias { get; } = new();
        public Func<PixEnvioRequest, Task>? AntesDaResposta { get; init; }
        public async Task<PixProviderResult> EnviarAsync(PixEnvioRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _envios);
            Referencias.Enqueue(request.ReferenciaIdempotente);
            Efeitos.TryAdd(request.ReferenciaIdempotente, 0);
            if (AntesDaResposta is not null) await AntesDaResposta(request);
            return resposta;
        }
        public Task<PixProviderResult> ConsultarAsync(PixConsultaRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _consultas);
            throw new Xunit.Sdk.XunitException("Consulta inesperada: este ciclo deve somente recuperar Envio ou aplicar evidência.");
        }
    }
}

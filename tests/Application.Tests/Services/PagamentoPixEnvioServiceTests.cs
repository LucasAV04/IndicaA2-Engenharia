using Application.Interfaces.Providers;
using Application.Interfaces.Stores;
using Application.Models;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class PagamentoPixEnvioServiceTests
{
    [Fact]
    public async Task ProcessarEnvioAsync_QuandoPagamentoNaoExistir_DeveLancarExcecaoESemPreparar()
    {
        var pagamentos = new Mock<IPagamentoPixRepository>();
        var store = new Mock<IPagamentoPixEnvioStore>();
        pagamentos.Setup(x => x.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((PagamentoPix?)null);

        await Assert.ThrowsAsync<PagamentoPixNaoEncontradoException>(() =>
            CriarService(pagamentos, store, new Mock<IPixProvider>()).ProcessarEnvioAsync(Guid.NewGuid()));

        store.Verify(x => x.TentarPrepararEnvioAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoPreparacaoNaoForAdquirida_NaoDeveChamarProvider()
    {
        var contexto = CriarContexto(adquirido: false);

        var resultado = await contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.False(resultado.EnvioExecutado);
        contexto.Provider.Verify(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoCanceladoAntesDaPreparacao_NaoDeveChamarProvider()
    {
        var pagamentos = new Mock<IPagamentoPixRepository>();
        var store = new Mock<IPagamentoPixEnvioStore>();
        var provider = new Mock<IPixProvider>();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CriarService(pagamentos, store, provider).ProcessarEnvioAsync(Guid.NewGuid(), source.Token));

        store.Verify(x => x.TentarPrepararEnvioAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        provider.Verify(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(StatusPixProvider.Confirmado, ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(StatusPixProvider.FalhaConfirmada, ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    [InlineData(StatusPixProvider.Pendente, ResultadoOperacaoPagamentoPix.Pendente)]
    [InlineData(StatusPixProvider.Indeterminado, ResultadoOperacaoPagamentoPix.Indeterminado)]
    public async Task ProcessarEnvioAsync_QuandoProviderResponder_DeveFinalizarComMesmoLease(
        StatusPixProvider statusProvider, ResultadoOperacaoPagamentoPix resultadoEsperado)
    {
        var contexto = CriarContexto();
        contexto.Provider.Setup(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.Token))
            .ReturnsAsync(CriarResultado(statusProvider));
        contexto.Store.Setup(x => x.FinalizarEnvioAsync(
                contexto.Pagamento.Id, contexto.OperacaoId, contexto.LeaseId, resultadoEsperado,
                "provider-id", "provider-code", CancellationToken.None))
            .ReturnsAsync(new FinalizacaoEnvioPagamentoPixResult(true));

        var resultado = await contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.True(resultado.EnvioExecutado);
        Assert.Equal(resultadoEsperado, resultado.ResultadoOperacao);
        Assert.Null(typeof(ResultadoEnvioPagamentoPix).GetProperty("ChavePix"));
        contexto.Provider.Verify(x => x.EnviarAsync(
            It.Is<PixEnvioRequest>(r => r.ReferenciaIdempotente == contexto.Pagamento.Id.ToString("N") &&
                                       r.ChavePix == contexto.Pagamento.ChavePix), contexto.Token), Times.Once);
        contexto.Store.Verify(x => x.FinalizarEnvioAsync(
            contexto.Pagamento.Id, contexto.OperacaoId, contexto.LeaseId, resultadoEsperado,
            "provider-id", "provider-code", CancellationToken.None), Times.Once);
        contexto.Pagamentos.Verify(x => x.AtualizarAsync(It.IsAny<PagamentoPix>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoProviderFalhar_DeveRegistrarIndeterminadoComTokenNoneEPropagar()
    {
        var contexto = CriarContexto();
        contexto.Provider.Setup(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.Token))
            .ThrowsAsync(new InvalidOperationException("falha simulada"));
        contexto.Store.Setup(x => x.FinalizarEnvioAsync(
                contexto.Pagamento.Id, contexto.OperacaoId, contexto.LeaseId,
                ResultadoOperacaoPagamentoPix.Indeterminado, null, null, CancellationToken.None))
            .ReturnsAsync(new FinalizacaoEnvioPagamentoPixResult(true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token));

        contexto.Store.Verify(x => x.FinalizarEnvioAsync(
            contexto.Pagamento.Id, contexto.OperacaoId, contexto.LeaseId,
            ResultadoOperacaoPagamentoPix.Indeterminado, null, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoProviderForCancelado_DeveRegistrarIndeterminadoESemPagamento()
    {
        var contexto = CriarContexto();
        contexto.Provider.Setup(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.Token))
            .ThrowsAsync(new OperationCanceledException(contexto.Token));
        contexto.Store.Setup(x => x.FinalizarEnvioAsync(
                contexto.Pagamento.Id, contexto.OperacaoId, contexto.LeaseId,
                ResultadoOperacaoPagamentoPix.Indeterminado, null, null, CancellationToken.None))
            .ReturnsAsync(new FinalizacaoEnvioPagamentoPixResult(true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token));

        contexto.Store.Verify(x => x.FinalizarEnvioAsync(
            contexto.Pagamento.Id, contexto.OperacaoId, contexto.LeaseId,
            ResultadoOperacaoPagamentoPix.Indeterminado, null, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoFinalizacaoPerderLease_NaoDeveReenviar()
    {
        var contexto = CriarContexto();
        contexto.Provider.Setup(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.Token))
            .ReturnsAsync(PixProviderResult.Confirmado());
        contexto.Store.Setup(x => x.FinalizarEnvioAsync(
                contexto.Pagamento.Id, contexto.OperacaoId, contexto.LeaseId,
                ResultadoOperacaoPagamentoPix.Confirmado, null, null, CancellationToken.None))
            .ReturnsAsync(new FinalizacaoEnvioPagamentoPixResult(false));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token));

        contexto.Provider.Verify(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.Token), Times.Once);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoReferenciaPreparadaNaoCorresponderAoPagamento_DeveFalharAntesDoProvider()
    {
        var contexto = CriarContexto();
        contexto.Store.Setup(x => x.TentarPrepararEnvioAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoEnvioPagamentoPixResult.AdquiridoCom(
                contexto.OperacaoId,
                1,
                contexto.LeaseId,
                Guid.NewGuid().ToString("N")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token));

        contexto.Provider.Verify(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoCanceladoAposResposta_DeveFinalizarAuditoriaComTokenNone()
    {
        var contexto = CriarContexto();
        contexto.Provider.Setup(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.Token))
            .Callback(() => contexto.CancellationSource.Cancel())
            .ReturnsAsync(PixProviderResult.Confirmado());
        contexto.Store.Setup(x => x.FinalizarEnvioAsync(
                contexto.Pagamento.Id,
                contexto.OperacaoId,
                contexto.LeaseId,
                ResultadoOperacaoPagamentoPix.Confirmado,
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new FinalizacaoEnvioPagamentoPixResult(true));

        var resultado = await contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.True(resultado.EnvioExecutado);
        contexto.Store.Verify(x => x.FinalizarEnvioAsync(
            contexto.Pagamento.Id,
            contexto.OperacaoId,
            contexto.LeaseId,
            ResultadoOperacaoPagamentoPix.Confirmado,
            null,
            null,
            CancellationToken.None), Times.Once);
    }

    private static Contexto CriarContexto(bool adquirido = true)
    {
        var pagamento = PagamentoPix.Criar(Guid.NewGuid(), Guid.NewGuid(), 12.34m, TipoChavePix.Email, "destinatario@exemplo.com");
        var pagamentos = new Mock<IPagamentoPixRepository>();
        var store = new Mock<IPagamentoPixEnvioStore>();
        var provider = new Mock<IPixProvider>();
        var cancellationSource = new CancellationTokenSource();
        var token = cancellationSource.Token;
        var operacaoId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        pagamentos.Setup(x => x.ObterPorIdAsync(pagamento.Id, token)).ReturnsAsync(pagamento);
        store.Setup(x => x.TentarPrepararEnvioAsync(pagamento.Id, token)).ReturnsAsync(
            adquirido
                ? PreparacaoEnvioPagamentoPixResult.AdquiridoCom(
                    operacaoId, 1, leaseId, pagamento.Id.ToString("N"))
                : PreparacaoEnvioPagamentoPixResult.NaoAdquirido());
        if (adquirido)
            pagamento.IniciarTentativa();
        return new Contexto(
            CriarService(pagamentos, store, provider),
            pagamento,
            pagamentos,
            store,
            provider,
            cancellationSource,
            token,
            operacaoId,
            leaseId);
    }

    private static PagamentoPixEnvioService CriarService(
        Mock<IPagamentoPixRepository> pagamentos, Mock<IPagamentoPixEnvioStore> store, Mock<IPixProvider> provider) =>
        new(pagamentos.Object, store.Object, provider.Object);

    private static PixProviderResult CriarResultado(StatusPixProvider status) => status switch
    {
        StatusPixProvider.Confirmado => PixProviderResult.Confirmado("provider-id", "provider-code"),
        StatusPixProvider.FalhaConfirmada => PixProviderResult.FalhaConfirmada("provider-id", "provider-code"),
        StatusPixProvider.Pendente => PixProviderResult.Pendente("provider-id", "provider-code"),
        _ => PixProviderResult.Indeterminado("provider-id", "provider-code")
    };

    private sealed record Contexto(
        PagamentoPixEnvioService Service, PagamentoPix Pagamento, Mock<IPagamentoPixRepository> Pagamentos,
        Mock<IPagamentoPixEnvioStore> Store,
        Mock<IPixProvider> Provider, CancellationTokenSource CancellationSource, CancellationToken Token,
        Guid OperacaoId, Guid LeaseId);
}

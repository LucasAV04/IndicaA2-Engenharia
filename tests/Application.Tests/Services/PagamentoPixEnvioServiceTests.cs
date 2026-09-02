using Application.Interfaces.Providers;
using Application.Interfaces.Stores;
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
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        var store = new Mock<IPagamentoPixEnvioStore>();
        pagamentoRepository
            .Setup(repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoPix?)null);

        await Assert.ThrowsAsync<PagamentoPixNaoEncontradoException>(() =>
            CriarService(pagamentoRepository, store, new Mock<IOperacaoPagamentoPixRepository>(), new Mock<IPixProvider>())
                .ProcessarEnvioAsync(Guid.NewGuid()));

        store.Verify(
            value => value.TentarPrepararEnvioAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoPreparacaoNaoForAdquirida_NaoDeveChamarProvider()
    {
        var pagamento = CriarPagamento();
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        var store = new Mock<IPagamentoPixEnvioStore>();
        var provider = new Mock<IPixProvider>();
        ConfigurarPagamentoExistente(pagamentoRepository, pagamento);
        store
            .Setup(value => value.TentarPrepararEnvioAsync(pagamento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Models.PreparacaoEnvioPagamentoPixResult.NaoAdquirido());

        var resultado = await CriarService(
                pagamentoRepository,
                store,
                new Mock<IOperacaoPagamentoPixRepository>(),
                provider)
            .ProcessarEnvioAsync(pagamento.Id);

        Assert.False(resultado.EnvioExecutado);
        Assert.Equal(pagamento.Id, resultado.PagamentoPixId);
        Assert.Null(resultado.OperacaoPagamentoPixId);
        provider.Verify(value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        VerificarSemMutacaoPagamento(pagamentoRepository);
    }

    [Theory]
    [InlineData(StatusPixProvider.Confirmado, ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(StatusPixProvider.FalhaConfirmada, ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    [InlineData(StatusPixProvider.Pendente, ResultadoOperacaoPagamentoPix.Pendente)]
    [InlineData(StatusPixProvider.Indeterminado, ResultadoOperacaoPagamentoPix.Indeterminado)]
    public async Task ProcessarEnvioAsync_QuandoProviderResponder_DeveFinalizarAuditoriaSemAlterarPagamento(
        StatusPixProvider statusProvider,
        ResultadoOperacaoPagamentoPix resultadoEsperado)
    {
        var contexto = CriarContextoPreparado();
        PixEnvioRequest? requestCapturado = null;
        contexto.Provider
            .Setup(value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.CancellationToken))
            .Callback<PixEnvioRequest, CancellationToken>((request, _) => requestCapturado = request)
            .ReturnsAsync(CriarResultadoProvider(statusProvider));
        contexto.OperacaoRepository
            .Setup(value => value.FinalizarAsync(contexto.Operacao, contexto.CancellationToken))
            .ReturnsAsync(true);

        var resultado = await contexto.Service.ProcessarEnvioAsync(
            contexto.Pagamento.Id,
            contexto.CancellationToken);

        Assert.True(resultado.EnvioExecutado);
        Assert.Equal(contexto.Pagamento.Id, resultado.PagamentoPixId);
        Assert.Equal(contexto.Operacao.Id, resultado.OperacaoPagamentoPixId);
        Assert.Equal(1, resultado.NumeroTentativaEnvio);
        Assert.Equal(resultadoEsperado, resultado.ResultadoOperacao);
        Assert.NotNull(requestCapturado);
        Assert.Equal(contexto.Pagamento.Id, requestCapturado!.PagamentoPixId);
        Assert.Equal(contexto.Pagamento.Valor, requestCapturado.Valor);
        Assert.Equal(contexto.Pagamento.TipoChavePix, requestCapturado.TipoChavePix);
        Assert.Equal(contexto.Pagamento.ChavePix, requestCapturado.ChavePix);
        Assert.Equal(contexto.Pagamento.Id.ToString("N"), requestCapturado.ReferenciaIdempotente);
        Assert.Equal(resultadoEsperado, contexto.Operacao.Resultado);
        Assert.Equal("provider-id", contexto.Operacao.IdentificadorProvider);
        Assert.Equal("provider-code", contexto.Operacao.Codigo);
        Assert.Equal(StatusPagamentoPix.Processando, contexto.Pagamento.Status);
        Assert.Equal(1, contexto.Pagamento.QuantidadeTentativas);
        Assert.Null(typeof(Application.Models.ResultadoEnvioPagamentoPix).GetProperty("ChavePix"));
        Assert.DoesNotContain(contexto.Pagamento.ChavePix, resultado.ToString());
        contexto.Provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.CancellationToken),
            Times.Once);
        VerificarSemMutacaoPagamento(contexto.PagamentoRepository);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoCanceladoAntesDaPreparacao_NaoDeveChamarProvider()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        var store = new Mock<IPagamentoPixEnvioStore>();
        var provider = new Mock<IPixProvider>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CriarService(pagamentoRepository, store, new Mock<IOperacaoPagamentoPixRepository>(), provider)
                .ProcessarEnvioAsync(Guid.NewGuid(), cancellationTokenSource.Token));

        pagamentoRepository.Verify(
            value => value.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        store.Verify(
            value => value.TentarPrepararEnvioAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        provider.Verify(value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoProviderCancelar_DeveManterAuditoriaAbertaESemRetry()
    {
        var contexto = CriarContextoPreparado();
        contexto.Provider
            .Setup(value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.CancellationToken))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        Assert.False(contexto.Operacao.FinishedAt.HasValue);
        Assert.Null(contexto.Operacao.Resultado);
        contexto.Provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.CancellationToken),
            Times.Once);
        contexto.OperacaoRepository.Verify(
            value => value.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerificarSemMutacaoPagamento(contexto.PagamentoRepository);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoProviderLancarExcecaoInesperada_DeveManterAuditoriaAberta()
    {
        var contexto = CriarContextoPreparado();
        contexto.Provider
            .Setup(value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("falha simulada do adapter"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        Assert.False(contexto.Operacao.FinishedAt.HasValue);
        Assert.Null(contexto.Operacao.Resultado);
        contexto.Provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.CancellationToken),
            Times.Once);
        contexto.OperacaoRepository.Verify(
            value => value.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerificarSemMutacaoPagamento(contexto.PagamentoRepository);
    }

    [Fact]
    public async Task ProcessarEnvioAsync_QuandoFinalizacaoFalhar_NaoDeveReenviarPix()
    {
        var contexto = CriarContextoPreparado();
        contexto.Provider
            .Setup(value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.CancellationToken))
            .ReturnsAsync(PixProviderResult.Confirmado());
        contexto.OperacaoRepository
            .Setup(value => value.FinalizarAsync(contexto.Operacao, contexto.CancellationToken))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        contexto.Provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), contexto.CancellationToken),
            Times.Once);
        Assert.True(contexto.Operacao.FinishedAt.HasValue);
        VerificarSemMutacaoPagamento(contexto.PagamentoRepository);
    }

    private static PixProviderResult CriarResultadoProvider(StatusPixProvider status) =>
        status switch
        {
            StatusPixProvider.Confirmado => PixProviderResult.Confirmado("provider-id", "provider-code"),
            StatusPixProvider.FalhaConfirmada => PixProviderResult.FalhaConfirmada("provider-id", "provider-code"),
            StatusPixProvider.Pendente => PixProviderResult.Pendente("provider-id", "provider-code"),
            StatusPixProvider.Indeterminado => PixProviderResult.Indeterminado("provider-id", "provider-code"),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static ContextoPreparado CriarContextoPreparado()
    {
        var pagamento = CriarPagamento();
        pagamento.IniciarTentativa();
        var operacao = OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1);
        var cancellationToken = new CancellationTokenSource().Token;
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        var store = new Mock<IPagamentoPixEnvioStore>();
        var operacaoRepository = new Mock<IOperacaoPagamentoPixRepository>();
        var provider = new Mock<IPixProvider>();

        pagamentoRepository
            .Setup(value => value.ObterPorIdAsync(pagamento.Id, cancellationToken))
            .ReturnsAsync(pagamento);
        store
            .Setup(value => value.TentarPrepararEnvioAsync(pagamento.Id, cancellationToken))
            .ReturnsAsync(Application.Models.PreparacaoEnvioPagamentoPixResult.AdquiridoCom(operacao.Id, 1));
        operacaoRepository
            .Setup(value => value.ObterPorIdAsync(operacao.Id, cancellationToken))
            .ReturnsAsync(operacao);

        return new ContextoPreparado(
            CriarService(pagamentoRepository, store, operacaoRepository, provider),
            pagamento,
            operacao,
            pagamentoRepository,
            operacaoRepository,
            provider,
            cancellationToken);
    }

    private static void ConfigurarPagamentoExistente(
        Mock<IPagamentoPixRepository> pagamentoRepository,
        PagamentoPix pagamento) =>
        pagamentoRepository
            .Setup(value => value.ObterPorIdAsync(pagamento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);

    private static PagamentoPixEnvioService CriarService(
        Mock<IPagamentoPixRepository> pagamentoRepository,
        Mock<IPagamentoPixEnvioStore> store,
        Mock<IOperacaoPagamentoPixRepository> operacaoRepository,
        Mock<IPixProvider> provider) =>
        new(pagamentoRepository.Object, store.Object, operacaoRepository.Object, provider.Object);

    private static PagamentoPix CriarPagamento() =>
        PagamentoPix.Criar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            123.45m,
            TipoChavePix.Email,
            "beneficiario@exemplo.com");

    private static void VerificarSemMutacaoPagamento(Mock<IPagamentoPixRepository> pagamentoRepository)
    {
        pagamentoRepository.Verify(
            value => value.AtualizarAsync(It.IsAny<PagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
        pagamentoRepository.Verify(
            value => value.TentarIniciarProcessamentoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed record ContextoPreparado(
        PagamentoPixEnvioService Service,
        PagamentoPix Pagamento,
        OperacaoPagamentoPix Operacao,
        Mock<IPagamentoPixRepository> PagamentoRepository,
        Mock<IOperacaoPagamentoPixRepository> OperacaoRepository,
        Mock<IPixProvider> Provider,
        CancellationToken CancellationToken);
}

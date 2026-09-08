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

public sealed class PagamentoPixAplicacaoResultadoServiceTests
{
    [Fact]
    public async Task AplicarAsync_QuandoIdentificadorForVazio_DeveRejeitarSemAcessarDependencias()
    {
        var contexto = CriarContexto();

        await Assert.ThrowsAsync<ArgumentException>(() => contexto.Service.AplicarAsync(Guid.Empty));

        contexto.Store.Verify(value => value.AplicarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AplicarAsync_QuandoPagamentoNaoExistir_DeveLancarExcecaoEspecifica()
    {
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        pagamentoRepository
            .Setup(value => value.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoPix?)null);
        var service = new PagamentoPixAplicacaoResultadoService(
            pagamentoRepository.Object,
            Mock.Of<ICashbackRepository>(),
            Mock.Of<IPagamentoPixAplicacaoResultadoStore>());

        await Assert.ThrowsAsync<PagamentoPixNaoEncontradoException>(() =>
            service.AplicarAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task AplicarAsync_QuandoStoreEncontrarConsultaAberta_DeveRequererReconciliacao()
    {
        var contexto = CriarContexto();
        contexto.Store
            .Setup(value => value.AplicarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoPersistenciaAplicacaoPagamentoPix.RequerReconciliacao());

        var resultado = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusAplicacaoPagamentoPix.RequerReconciliacao, resultado.Status);
        contexto.Store.Verify(value => value.AplicarAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
    }

    [Fact]
    public async Task AplicarAsync_QuandoStoreNaoEncontrarEvidencia_DeveRetornarSemResultado()
    {
        var contexto = CriarContexto();
        contexto.Store
            .Setup(value => value.AplicarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoPersistenciaAplicacaoPagamentoPix.SemResultadoConclusivo());

        var resultado = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusAplicacaoPagamentoPix.SemResultadoConclusivo, resultado.Status);
        Assert.Null(resultado.ResultadoOperacao);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    public async Task AplicarAsync_QuandoStoreAplicar_DevePropagarEvidenciaConclusiva(ResultadoOperacaoPagamentoPix evidencia)
    {
        var contexto = CriarContexto();
        contexto.Store
            .Setup(value => value.AplicarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoPersistenciaAplicacaoPagamentoPix.Aplicado(evidencia));

        var resultado = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusAplicacaoPagamentoPix.Aplicado, resultado.Status);
        Assert.Equal(evidencia, resultado.ResultadoOperacao);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    public async Task AplicarAsync_QuandoStoreInformarJaAplicado_DeveSerIdempotente(ResultadoOperacaoPagamentoPix evidencia)
    {
        var contexto = CriarContexto();
        contexto.Store
            .Setup(value => value.AplicarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoPersistenciaAplicacaoPagamentoPix.JaAplicado(evidencia));

        var resultado = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusAplicacaoPagamentoPix.JaAplicado, resultado.Status);
        Assert.Equal(evidencia, resultado.ResultadoOperacao);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task AplicarAsync_QuandoSnapshotsDivergirem_DeveFalharAntesDoStore(
        bool beneficiario, bool valor, bool cashback)
    {
        var contexto = CriarContexto(beneficiario, valor, cashback);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.Token));

        contexto.Store.Verify(value => value.AplicarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Servico_NaoDeveDependerDeIPixProviderOuAuditoria()
    {
        var dependencias = typeof(PagamentoPixAplicacaoResultadoService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(IPixProvider), dependencias);
        Assert.DoesNotContain(typeof(IOperacaoPagamentoPixRepository), dependencias);
    }

    [Fact]
    public async Task AplicarAsync_QuandoStoreFalhar_DevePropagarSemMutacaoLocal()
    {
        var contexto = CriarContexto();
        contexto.Store.Setup(x => x.AplicarAsync(contexto.Pagamento.Id, contexto.Token))
            .ThrowsAsync(new InvalidOperationException("conflito simulado"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.Token));
        Assert.Equal(StatusPagamentoPix.Processando, contexto.Pagamento.Status);
    }

    [Fact]
    public async Task AplicarAsync_QuandoCancelado_NaoDeveAcessarStore()
    {
        var contexto = CriarContexto();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            contexto.Service.AplicarAsync(contexto.Pagamento.Id, source.Token));
        contexto.Store.Verify(x => x.AplicarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Contexto CriarContexto(bool beneficiarioDivergente = false, bool valorDivergente = false, bool cashbackDivergente = false)
    {
        var cashback = Cashback.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m);
        cashback.Aprovar();
        var pagamento = PagamentoPix.Criar(
            cashbackDivergente ? Guid.NewGuid() : cashback.Id,
            beneficiarioDivergente ? Guid.NewGuid() : cashback.UsuarioIndicadorId,
            valorDivergente ? cashback.Valor + 1m : cashback.Valor,
            TipoChavePix.Email,
            "snapshot@exemplo.com");
        pagamento.IniciarTentativa();
        var token = new CancellationTokenSource().Token;
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        var cashbackRepository = new Mock<ICashbackRepository>();
        var store = new Mock<IPagamentoPixAplicacaoResultadoStore>();
        pagamentoRepository.Setup(value => value.ObterPorIdAsync(pagamento.Id, token)).ReturnsAsync(pagamento);
        cashbackRepository.Setup(value => value.ObterPorIdAsync(pagamento.CashbackId, token)).ReturnsAsync(cashback);

        return new Contexto(
            new PagamentoPixAplicacaoResultadoService(
                pagamentoRepository.Object,
                cashbackRepository.Object,
                store.Object),
            pagamento,
            store,
            token);
    }

    private sealed record Contexto(
        PagamentoPixAplicacaoResultadoService Service,
        PagamentoPix Pagamento,
        Mock<IPagamentoPixAplicacaoResultadoStore> Store,
        CancellationToken Token);
}

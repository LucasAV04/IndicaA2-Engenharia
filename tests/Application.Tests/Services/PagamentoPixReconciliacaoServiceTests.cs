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

public sealed class PagamentoPixReconciliacaoServiceTests
{
    [Fact]
    public async Task ReconciliarAsync_QuandoIdentificadorForVazio_DeveRejeitar()
    {
        var contexto = CriarContexto();

        await Assert.ThrowsAsync<ArgumentException>(() => contexto.Service.ReconciliarAsync(Guid.Empty));

        contexto.Store.Verify(value => value.PrepararConsultaAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoPagamentoNaoExistir_DeveLancarExcecaoEspecifica()
    {
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        pagamentoRepository.Setup(value => value.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoPix?)null);
        var service = new PagamentoPixReconciliacaoService(
            pagamentoRepository.Object,
            Mock.Of<IOperacaoPagamentoPixRepository>(),
            Mock.Of<IPagamentoPixReconciliacaoStore>(),
            Mock.Of<IPixProvider>());

        await Assert.ThrowsAsync<PagamentoPixNaoEncontradoException>(() =>
            service.ReconciliarAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoAplicacaoJaVenceuCoordenacao_NaoDeveConsultarProvider()
    {
        var contexto = CriarContexto();
        contexto.Store
            .Setup(value => value.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.NaoAplicavel());

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusReconciliacaoPagamentoPix.NaoAplicavel, resultado.Status);
        VerificarNenhumaChamadaProvider(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoOutraConsultaEstiverAberta_NaoDeveConsultarProvider()
    {
        var contexto = CriarContexto();
        contexto.Store
            .Setup(value => value.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.ConsultaEmAndamento());

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusReconciliacaoPagamentoPix.ConsultaEmAndamento, resultado.Status);
        VerificarNenhumaChamadaProvider(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoEvidenciaJaExistir_NaoDeveCriarNovaConsultaNemChamarProvider()
    {
        var contexto = CriarContexto();
        contexto.Store
            .Setup(value => value.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.JaConclusivo(
                ResultadoOperacaoPagamentoPix.Confirmado,
                operacaoEnvioAbertaResolvida: true));

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo, resultado.Status);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, resultado.ResultadoOperacao);
        Assert.True(resultado.OperacaoEnvioAbertaResolvida);
        VerificarNenhumaChamadaProvider(contexto);
    }

    [Theory]
    [InlineData(StatusPixProvider.Confirmado, ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(StatusPixProvider.FalhaConfirmada, ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    [InlineData(StatusPixProvider.Pendente, ResultadoOperacaoPagamentoPix.Pendente)]
    [InlineData(StatusPixProvider.Indeterminado, ResultadoOperacaoPagamentoPix.Indeterminado)]
    public async Task ReconciliarAsync_QuandoConsultaForPreparada_DeveChamarProviderUmaVezEFinalizarAuditoria(
        StatusPixProvider statusProvider,
        ResultadoOperacaoPagamentoPix resultadoEsperado)
    {
        var contexto = CriarContexto();
        var consulta = OperacaoPagamentoPix.IniciarConsulta(contexto.Pagamento.Id);
        contexto.Store
            .Setup(value => value.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(consulta.Id));
        contexto.Operacoes
            .Setup(value => value.ObterPorIdAsync(consulta.Id, contexto.Token))
            .ReturnsAsync(consulta);
        contexto.Provider
            .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token))
            .ReturnsAsync(CriarResultadoProvider(statusProvider));
        contexto.Operacoes
            .Setup(value => value.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), CancellationToken.None))
            .ReturnsAsync(true);
        contexto.Operacoes
            .Setup(value => value.ObterPorPagamentoPixIdAsync(contexto.Pagamento.Id, CancellationToken.None))
            .ReturnsAsync([OperacaoPagamentoPix.Reidratar(
                Guid.NewGuid(),
                contexto.Pagamento.Id,
                TipoOperacaoPagamentoPix.Envio,
                1,
                contexto.Pagamento.Id.ToString("N"),
                ResultadoOperacaoPagamentoPix.Pendente,
                null,
                null,
                DateTime.UtcNow.AddMinutes(-2),
                DateTime.UtcNow.AddMinutes(-1),
                DateTime.UtcNow.AddMinutes(-1))]);

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
        Assert.Equal(resultadoEsperado, resultado.ResultadoOperacao);
        contexto.Provider.Verify(value => value.ConsultarAsync(
            It.Is<PixConsultaRequest>(request => request.PagamentoPixId == contexto.Pagamento.Id),
            contexto.Token), Times.Once);
        contexto.Operacoes.Verify(value => value.FinalizarAsync(
            It.Is<OperacaoPagamentoPix>(operacao =>
                operacao.Id == consulta.Id && operacao.Resultado == resultadoEsperado),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public void Servico_DeveDependerDaPreparacaoPersistenteENaoDeveEnviarPix()
    {
        var dependencias = typeof(PagamentoPixReconciliacaoService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType);

        Assert.Contains(typeof(IPagamentoPixReconciliacaoStore), dependencias);
        Assert.Contains(typeof(IPixProvider), dependencias);
    }

    private static Contexto CriarContexto()
    {
        var pagamento = PagamentoPix.Criar(
            Guid.NewGuid(), Guid.NewGuid(), 50m, TipoChavePix.Email, "beneficiario@exemplo.com");
        pagamento.IniciarTentativa();
        var token = new CancellationTokenSource().Token;
        var pagamentos = new Mock<IPagamentoPixRepository>();
        var operacoes = new Mock<IOperacaoPagamentoPixRepository>();
        var store = new Mock<IPagamentoPixReconciliacaoStore>();
        var provider = new Mock<IPixProvider>();
        pagamentos.Setup(value => value.ObterPorIdAsync(pagamento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pagamento);

        return new Contexto(
            new PagamentoPixReconciliacaoService(pagamentos.Object, operacoes.Object, store.Object, provider.Object),
            pagamento,
            operacoes,
            store,
            provider,
            token);
    }

    private static void VerificarNenhumaChamadaProvider(Contexto contexto) =>
        contexto.Provider.Verify(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), It.IsAny<CancellationToken>()), Times.Never);

    private static PixProviderResult CriarResultadoProvider(StatusPixProvider status) =>
        status switch
        {
            StatusPixProvider.Confirmado => PixProviderResult.Confirmado("provider-id", "codigo"),
            StatusPixProvider.FalhaConfirmada => PixProviderResult.FalhaConfirmada("provider-id", "codigo"),
            StatusPixProvider.Pendente => PixProviderResult.Pendente("provider-id", "codigo"),
            StatusPixProvider.Indeterminado => PixProviderResult.Indeterminado("provider-id", "codigo"),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private sealed record Contexto(
        PagamentoPixReconciliacaoService Service,
        PagamentoPix Pagamento,
        Mock<IOperacaoPagamentoPixRepository> Operacoes,
        Mock<IPagamentoPixReconciliacaoStore> Store,
        Mock<IPixProvider> Provider,
        CancellationToken Token);
}

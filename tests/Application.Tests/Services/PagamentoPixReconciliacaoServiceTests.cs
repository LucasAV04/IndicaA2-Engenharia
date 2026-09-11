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
    public async Task ReconciliarAsync_QuandoEnvioEstiverEmAndamento_NaoDeveConsultarProvider()
    {
        var contexto = CriarContexto();
        contexto.Store.Setup(value => value.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.EnvioEmAndamento());

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusReconciliacaoPagamentoPix.EnvioEmAndamento, resultado.Status);
        VerificarNenhumaChamadaProvider(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoEnvioExigirRecuperacaoIdempotente_NaoDeveConsultarProvider()
    {
        var contexto = CriarContexto();
        contexto.Store.Setup(value => value.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.EnvioPendenteRecuperacao());

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusReconciliacaoPagamentoPix.EnvioPendenteRecuperacao, resultado.Status);
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
        var leaseId = Guid.NewGuid();
        contexto.Store
            .Setup(value => value.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(consulta.Id, leaseId));
        contexto.Operacoes
            .Setup(value => value.ObterPorIdAsync(consulta.Id, contexto.Token))
            .ReturnsAsync(consulta);
        contexto.Provider
            .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token))
            .ReturnsAsync(CriarResultadoProvider(statusProvider));
        contexto.Store
            .Setup(value => value.FinalizarConsultaAsync(
                contexto.Pagamento.Id,
                consulta.Id,
                leaseId,
                It.IsAny<ResultadoOperacaoPagamentoPix>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                CancellationToken.None))
            .ReturnsAsync(new FinalizacaoConsultaPagamentoPixResult(true, false));
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
        contexto.Store.Verify(value => value.FinalizarConsultaAsync(
            contexto.Pagamento.Id,
            consulta.Id,
            leaseId,
            resultadoEsperado,
            "provider-id",
            "codigo",
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoProviderFalhar_DeveFinalizarConsultaComoIndeterminadaELiberarRecuperacao()
    {
        var contexto = CriarContexto();
        var consulta = OperacaoPagamentoPix.IniciarConsulta(contexto.Pagamento.Id);
        var leaseId = Guid.NewGuid();
        contexto.Store.Setup(value => value.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(consulta.Id, leaseId));
        contexto.Operacoes.Setup(value => value.ObterPorIdAsync(consulta.Id, contexto.Token)).ReturnsAsync(consulta);
        contexto.Provider.Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token))
            .ThrowsAsync(new HttpRequestException("falha simulada"));
        contexto.Store.Setup(value => value.FinalizarConsultaAsync(
                contexto.Pagamento.Id, consulta.Id, leaseId, ResultadoOperacaoPagamentoPix.Indeterminado,
                null, null, CancellationToken.None))
            .ReturnsAsync(new FinalizacaoConsultaPagamentoPixResult(true, false));

        await Assert.ThrowsAsync<HttpRequestException>(() => contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token));

        contexto.Store.Verify(value => value.FinalizarConsultaAsync(
            contexto.Pagamento.Id, consulta.Id, leaseId, ResultadoOperacaoPagamentoPix.Indeterminado,
            null, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoTokenDoLeaseForPerdido_NaoDeveSobrescreverAuditoria()
    {
        var contexto = CriarContexto();
        var consulta = OperacaoPagamentoPix.IniciarConsulta(contexto.Pagamento.Id);
        var leaseId = Guid.NewGuid();
        contexto.Store.Setup(value => value.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(consulta.Id, leaseId));
        contexto.Operacoes.Setup(value => value.ObterPorIdAsync(consulta.Id, contexto.Token)).ReturnsAsync(consulta);
        contexto.Provider.Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token))
            .ReturnsAsync(PixProviderResult.Confirmado("provider", "code"));
        contexto.Store.Setup(value => value.FinalizarConsultaAsync(
                contexto.Pagamento.Id, consulta.Id, leaseId, ResultadoOperacaoPagamentoPix.Confirmado,
                "provider", "code", CancellationToken.None))
            .ReturnsAsync(new FinalizacaoConsultaPagamentoPixResult(false, false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token));
    }

    [Fact]
    public void Servico_DeveDependerDaPreparacaoPersistenteENaoDeveEnviarPix()
    {
        var dependencias = typeof(PagamentoPixReconciliacaoService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType);

        Assert.Contains(typeof(IPagamentoPixReconciliacaoStore), dependencias);
        Assert.Contains(typeof(IPixProvider), dependencias);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoPreparacaoFalhar_NaoDeveChamarProvider()
    {
        var contexto = CriarContexto();
        contexto.Store.Setup(x => x.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ThrowsAsync(new InvalidOperationException("falha de persistência"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token));
        VerificarNenhumaChamadaProvider(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoAuditoriaPreparadaNaoExistir_NaoDeveChamarProvider()
    {
        var contexto = CriarContexto();
        contexto.Store.Setup(x => x.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(Guid.NewGuid(), Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token));
        VerificarNenhumaChamadaProvider(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoProviderCancelar_DeveAuditarIndeterminadoSemCancelarPersistencia()
    {
        var (contexto, consulta, lease) = PrepararConsulta();
        contexto.Provider.Setup(x => x.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token))
            .ThrowsAsync(new OperationCanceledException(contexto.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token));
        contexto.Store.Verify(x => x.FinalizarConsultaAsync(contexto.Pagamento.Id, consulta.Id, lease,
            ResultadoOperacaoPagamentoPix.Indeterminado, null, null, CancellationToken.None), Times.Once);
        VerificarAusenciaDeEscritaDireta(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoFinalizacaoFalhar_NaoDeveReconsultarOuEscreverForaDoLease()
    {
        var (contexto, consulta, lease) = PrepararConsulta();
        contexto.Store.Setup(x => x.FinalizarConsultaAsync(contexto.Pagamento.Id, consulta.Id, lease,
            It.IsAny<ResultadoOperacaoPagamentoPix>(), It.IsAny<string?>(), It.IsAny<string?>(), CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("falha simulada"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token));
        contexto.Provider.Verify(x => x.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token), Times.Once);
        VerificarAusenciaDeEscritaDireta(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoProviderEAuditoriaFalharem_DeveExporFalhaDePersistencia()
    {
        var (contexto, consulta, lease) = PrepararConsulta();
        contexto.Provider.Setup(x => x.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token))
            .ThrowsAsync(new HttpRequestException("simulada"));
        contexto.Store.Setup(x => x.FinalizarConsultaAsync(contexto.Pagamento.Id, consulta.Id, lease,
            ResultadoOperacaoPagamentoPix.Indeterminado, null, null, CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("auditoria indisponível"));
        var falha = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token));
        Assert.Equal("auditoria indisponível", falha.Message);
        VerificarAusenciaDeEscritaDireta(contexto);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReconciliarAsync_DevePropagarRecuperacaoAtomicaDoEnvio(bool envioRecuperado)
    {
        var (contexto, consulta, lease) = PrepararConsulta();
        contexto.Store.Setup(x => x.FinalizarConsultaAsync(contexto.Pagamento.Id, consulta.Id, lease,
            It.IsAny<ResultadoOperacaoPagamentoPix>(), It.IsAny<string?>(), It.IsAny<string?>(), CancellationToken.None))
            .ReturnsAsync(new FinalizacaoConsultaPagamentoPixResult(true, envioRecuperado));
        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token);
        Assert.Equal(envioRecuperado, resultado.OperacaoEnvioAbertaResolvida);
        VerificarAusenciaDeEscritaDireta(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoProviderBloqueado_DeveEsperarAuditoriaPreparadaAntesDaChamada()
    {
        var (contexto, consulta, lease) = PrepararConsulta();
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource<PixProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        contexto.Provider.Setup(x => x.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token))
            .Callback(() =>
            {
                contexto.Store.Verify(x => x.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
                contexto.Operacoes.Verify(x => x.ObterPorIdAsync(consulta.Id, contexto.Token), Times.Once);
                entrou.SetResult();
            }).Returns(liberar.Task);
        var execucao = contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token);
        await entrou.Task.WaitAsync(TimeSpan.FromSeconds(5));
        contexto.Store.Verify(x => x.FinalizarConsultaAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<ResultadoOperacaoPagamentoPix>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        liberar.SetResult(PixProviderResult.Pendente());
        await execucao;
        VerificarAusenciaDeEscritaDireta(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoCancelamentoAposResposta_DevePersistirComTokenNone()
    {
        using var cancelamento = new CancellationTokenSource();
        var contexto = CriarContexto(cancelamento.Token);
        var consulta = OperacaoPagamentoPix.IniciarConsulta(contexto.Pagamento.Id);
        var lease = Guid.NewGuid();
        contexto.Store.Setup(x => x.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(consulta.Id, lease));
        contexto.Operacoes.Setup(x => x.ObterPorIdAsync(consulta.Id, contexto.Token)).ReturnsAsync(consulta);
        contexto.Provider.Setup(x => x.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token))
            .Callback(cancelamento.Cancel).ReturnsAsync(PixProviderResult.Pendente());
        contexto.Store.Setup(x => x.FinalizarConsultaAsync(contexto.Pagamento.Id, consulta.Id, lease,
            ResultadoOperacaoPagamentoPix.Pendente, null, null, CancellationToken.None))
            .ReturnsAsync(new FinalizacaoConsultaPagamentoPixResult(true, false));
        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado,
            (await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token)).Status);
    }

    private static (Contexto Contexto, OperacaoPagamentoPix Consulta, Guid Lease) PrepararConsulta()
    {
        var contexto = CriarContexto();
        var consulta = OperacaoPagamentoPix.IniciarConsulta(contexto.Pagamento.Id);
        var lease = Guid.NewGuid();
        contexto.Store.Setup(x => x.PrepararConsultaAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(consulta.Id, lease));
        contexto.Operacoes.Setup(x => x.ObterPorIdAsync(consulta.Id, contexto.Token)).ReturnsAsync(consulta);
        contexto.Provider.Setup(x => x.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.Token))
            .ReturnsAsync(PixProviderResult.Confirmado("provider", "codigo"));
        contexto.Store.Setup(x => x.FinalizarConsultaAsync(contexto.Pagamento.Id, consulta.Id, lease,
            It.IsAny<ResultadoOperacaoPagamentoPix>(), It.IsAny<string?>(), It.IsAny<string?>(), CancellationToken.None))
            .ReturnsAsync(new FinalizacaoConsultaPagamentoPixResult(true, true));
        return (contexto, consulta, lease);
    }

    private static void VerificarAusenciaDeEscritaDireta(Contexto contexto)
    {
        contexto.Operacoes.Verify(x => x.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()), Times.Never);
        contexto.Operacoes.Verify(x => x.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()), Times.Never);
        contexto.Provider.Verify(x => x.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(StatusPagamentoPix.Processando, contexto.Pagamento.Status);
        Assert.Equal(1, contexto.Pagamento.QuantidadeTentativas);
    }

    private static Contexto CriarContexto(CancellationToken? cancellationToken = null)
    {
        var pagamento = PagamentoPix.Criar(
            Guid.NewGuid(), Guid.NewGuid(), 50m, TipoChavePix.Email, "beneficiario@exemplo.com");
        pagamento.IniciarTentativa();
        var token = cancellationToken ?? new CancellationTokenSource().Token;
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

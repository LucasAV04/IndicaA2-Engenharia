using Application.Interfaces.Providers;
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
    public async Task ReconciliarAsync_QuandoIdForVazio_NaoDeveAcessarDependencias()
    {
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CriarService(pagamentoRepository, new Mock<IOperacaoPagamentoPixRepository>(), new Mock<IPixProvider>())
                .ReconciliarAsync(Guid.Empty));

        pagamentoRepository.Verify(
            value => value.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoPagamentoNaoExistir_DeveLancarExcecaoEspecifica()
    {
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        pagamentoRepository
            .Setup(value => value.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoPix?)null);

        await Assert.ThrowsAsync<PagamentoPixNaoEncontradoException>(() =>
            CriarService(pagamentoRepository, new Mock<IOperacaoPagamentoPixRepository>(), new Mock<IPixProvider>())
                .ReconciliarAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(StatusPagamentoPix.Pendente, 0)]
    [InlineData(StatusPagamentoPix.Falhou, 1)]
    [InlineData(StatusPagamentoPix.FalhaDefinitiva, 5)]
    [InlineData(StatusPagamentoPix.Concluido, 1)]
    [InlineData(StatusPagamentoPix.Cancelado, 0)]
    public async Task ReconciliarAsync_QuandoStatusNaoForProcessando_DeveRetornarNaoAplicavel(
        StatusPagamentoPix status,
        int tentativas)
    {
        var pagamento = CriarPagamento(status, tentativas);
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        var operacaoRepository = new Mock<IOperacaoPagamentoPixRepository>();
        var provider = new Mock<IPixProvider>();
        pagamentoRepository
            .Setup(value => value.ObterPorIdAsync(pagamento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);

        var resultado = await CriarService(pagamentoRepository, operacaoRepository, provider)
            .ReconciliarAsync(pagamento.Id);

        Assert.Equal(StatusReconciliacaoPagamentoPix.NaoAplicavel, resultado.Status);
        operacaoRepository.Verify(
            value => value.ObterPorPagamentoPixIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerificarNenhumaChamadaExternaOuMutacao(pagamentoRepository, provider);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoProcessandoSemAuditoria_DeveLancarInconsistenciaSemConsultar()
    {
        var contexto = CriarContexto([]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        contexto.Provider.Verify(
            value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        contexto.OperacaoRepository.Verify(
            value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    public async Task ReconciliarAsync_QuandoHistoricoJaForConclusivo_NaoDeveConsultar(
        ResultadoOperacaoPagamentoPix resultadoConclusivo)
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 1);
        var envio = OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1);
        envio.Finalizar(resultadoConclusivo);
        var contexto = CriarContexto([envio], pagamento, envio);

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo, resultado.Status);
        Assert.Equal(resultadoConclusivo, resultado.ResultadoOperacao);
        contexto.Provider.Verify(
            value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        contexto.OperacaoRepository.Verify(
            value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoTentativaAnteriorFalhou_DeveConsultarSomenteATentativaAtual()
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 2);
        var inicio = DateTime.UtcNow.AddMinutes(-3);
        var envioAnterior = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada, inicio);
        var envioAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 2, null, inicio.AddMinutes(1));
        var contexto = CriarContexto([envioAnterior, envioAtual], pagamento, envioAtual);
        PrepararConsultaComResultado(contexto, PixProviderResult.Confirmado());

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
        Assert.Equal(ResultadoOperacaoPagamentoPix.FalhaConfirmada, envioAnterior.Resultado);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, envioAtual.Resultado);
        VerificarSomenteConsulta(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoConsultaConclusivaForDeCicloAnterior_DeveConsultarTentativaAtual()
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 2);
        var inicio = DateTime.UtcNow.AddMinutes(-4);
        var envioAnterior = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1,
            ResultadoOperacaoPagamentoPix.Pendente, inicio);
        var consultaAnterior = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada, inicio.AddMinutes(1));
        var envioAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 2, null, inicio.AddMinutes(2));
        var contexto = CriarContexto([envioAnterior, consultaAnterior, envioAtual], pagamento, envioAtual);
        PrepararConsultaComResultado(contexto, PixProviderResult.Pendente());

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
        Assert.Equal(ResultadoOperacaoPagamentoPix.FalhaConfirmada, consultaAnterior.Resultado);
        Assert.False(consultaAnterior.FinishedAt is null);
        VerificarSomenteConsulta(contexto);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    public async Task ReconciliarAsync_QuandoConsultaDoCicloAtualForConclusiva_NaoDeveConsultarNovamente(
        ResultadoOperacaoPagamentoPix resultadoConclusivo)
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 1);
        var inicio = DateTime.UtcNow.AddMinutes(-2);
        var envioAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1,
            ResultadoOperacaoPagamentoPix.Pendente, inicio);
        var consultaAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null,
            resultadoConclusivo, inicio.AddMinutes(1));
        var contexto = CriarContexto([envioAtual, consultaAtual], pagamento, envioAtual);

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo, resultado.Status);
        Assert.Equal(resultadoConclusivo, resultado.ResultadoOperacao);
        VerificarNenhumaConsultaNova(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoCicloAtualTiverResultadosConclusivosConflitantes_DeveLancarInconsistencia()
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 1);
        var inicio = DateTime.UtcNow.AddMinutes(-2);
        var envioAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1,
            ResultadoOperacaoPagamentoPix.Confirmado, inicio);
        var consultaAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada, inicio.AddMinutes(1));
        var contexto = CriarContexto([envioAtual, consultaAtual], pagamento, envioAtual);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        VerificarNenhumaConsultaNova(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoEnvioDaTentativaAtualEstiverAusente_DeveLancarInconsistencia()
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 2);
        var envioAnterior = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada, DateTime.UtcNow.AddMinutes(-2));
        var contexto = CriarContexto([envioAnterior], pagamento);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        VerificarNenhumaConsultaNova(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoHouverMaisDeUmEnvioDaTentativaAtual_DeveLancarInconsistencia()
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 1);
        var inicio = DateTime.UtcNow.AddMinutes(-2);
        var primeiroEnvioAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1, null, inicio);
        var segundoEnvioAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1, null, inicio.AddMinutes(1));
        var contexto = CriarContexto([primeiroEnvioAtual, segundoEnvioAtual], pagamento, primeiroEnvioAtual);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        VerificarNenhumaConsultaNova(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoEnvioAnteriorEstiverAberto_DeveLancarInconsistencia()
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 2);
        var inicio = DateTime.UtcNow.AddMinutes(-2);
        var envioAnteriorAberto = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1, null, inicio);
        var envioAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 2, null, inicio.AddMinutes(1));
        var contexto = CriarContexto([envioAnteriorAberto, envioAtual], pagamento, envioAtual);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        VerificarNenhumaConsultaNova(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoResultadoForConclusivo_DeveFinalizarSomenteEnvioAtual()
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 2);
        var inicio = DateTime.UtcNow.AddMinutes(-2);
        var envioAnterior = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1,
            ResultadoOperacaoPagamentoPix.Pendente, inicio);
        var envioAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 2, null, inicio.AddMinutes(1));
        var contexto = CriarContexto([envioAnterior, envioAtual], pagamento, envioAtual);
        PrepararConsultaComResultado(contexto, PixProviderResult.Confirmado());

        await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(ResultadoOperacaoPagamentoPix.Pendente, envioAnterior.Resultado);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, envioAtual.Resultado);
        VerificarSomenteConsulta(contexto);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoConsultaAntigaEstiverAberta_NaoDeveFinalizaLa()
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 2);
        var inicio = DateTime.UtcNow.AddMinutes(-3);
        var envioAnterior = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1,
            ResultadoOperacaoPagamentoPix.Pendente, inicio);
        var consultaAnteriorAberta = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null,
            null, inicio.AddMinutes(1));
        var envioAtual = CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 2, null, inicio.AddMinutes(2));
        var contexto = CriarContexto([envioAnterior, consultaAnteriorAberta, envioAtual], pagamento, envioAtual);
        PrepararConsultaComResultado(contexto, PixProviderResult.Confirmado());

        await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Null(consultaAnteriorAberta.Resultado);
        Assert.Null(consultaAnteriorAberta.FinishedAt);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, envioAtual.Resultado);
        VerificarSomenteConsulta(contexto);
    }

    [Theory]
    [InlineData(StatusPixProvider.Confirmado, ResultadoOperacaoPagamentoPix.Confirmado, true)]
    [InlineData(StatusPixProvider.FalhaConfirmada, ResultadoOperacaoPagamentoPix.FalhaConfirmada, true)]
    [InlineData(StatusPixProvider.Pendente, ResultadoOperacaoPagamentoPix.Pendente, false)]
    [InlineData(StatusPixProvider.Indeterminado, ResultadoOperacaoPagamentoPix.Indeterminado, false)]
    public async Task ReconciliarAsync_QuandoEnvioEstiverAberto_DeveAuditarConsultaEMapearResultado(
        StatusPixProvider statusProvider,
        ResultadoOperacaoPagamentoPix resultadoEsperado,
        bool envioDeveSerResolvido)
    {
        var contexto = CriarContextoComEnvioAberto();
        OperacaoPagamentoPix? consultaAdicionada = null;
        PixConsultaRequest? requestCapturado = null;
        contexto.OperacaoRepository
            .Setup(value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), contexto.CancellationToken))
            .Callback<OperacaoPagamentoPix, CancellationToken>((operacao, _) => consultaAdicionada = operacao)
            .Returns(Task.CompletedTask);
        contexto.Provider
            .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken))
            .Callback<PixConsultaRequest, CancellationToken>((request, _) => requestCapturado = request)
            .ReturnsAsync(CriarResultadoProvider(statusProvider));
        contexto.OperacaoRepository
            .Setup(value => value.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), CancellationToken.None))
            .ReturnsAsync(true);

        var resultado = await contexto.Service.ReconciliarAsync(
            contexto.Pagamento.Id,
            contexto.CancellationToken);

        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
        Assert.Equal(resultadoEsperado, resultado.ResultadoOperacao);
        Assert.Equal(envioDeveSerResolvido, resultado.OperacaoEnvioAbertaResolvida);
        Assert.NotNull(consultaAdicionada);
        Assert.Equal(TipoOperacaoPagamentoPix.Consulta, consultaAdicionada!.TipoOperacao);
        Assert.Null(consultaAdicionada.NumeroTentativaEnvio);
        Assert.Equal(contexto.Pagamento.Id.ToString("N"), consultaAdicionada.ReferenciaIdempotente);
        Assert.Equal(resultadoEsperado, consultaAdicionada.Resultado);
        Assert.Equal("provider-id", consultaAdicionada.IdentificadorProvider);
        Assert.Equal("provider-code", consultaAdicionada.Codigo);
        Assert.NotNull(requestCapturado);
        Assert.Equal(contexto.Pagamento.Id, requestCapturado!.PagamentoPixId);
        Assert.Equal(contexto.Pagamento.Id.ToString("N"), requestCapturado.ReferenciaIdempotente);
        Assert.Equal(envioDeveSerResolvido, contexto.EnvioAberto!.FinishedAt.HasValue);
        Assert.Equal(StatusPagamentoPix.Processando, contexto.Pagamento.Status);
        Assert.Equal(1, contexto.Pagamento.QuantidadeTentativas);
        Assert.Null(typeof(ResultadoReconciliacaoPagamentoPix).GetProperty("ChavePix"));
        contexto.Provider.Verify(
            value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken),
            Times.Once);
        contexto.Provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerificarNenhumaMutacaoPagamento(contexto.PagamentoRepository);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoConsultaAnteriorEstiverAberta_DevePermitirNovaConsulta()
    {
        var contexto = CriarContextoComEnvioAberto();
        var consultaAnterior = OperacaoPagamentoPix.IniciarConsulta(contexto.Pagamento.Id);
        contexto.Historico.Add(consultaAnterior);
        contexto.Provider
            .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken))
            .ReturnsAsync(PixProviderResult.Pendente());
        contexto.OperacaoRepository
            .Setup(value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), contexto.CancellationToken))
            .Returns(Task.CompletedTask);
        contexto.OperacaoRepository
            .Setup(value => value.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), CancellationToken.None))
            .ReturnsAsync(true);

        var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
        Assert.False(consultaAnterior.FinishedAt.HasValue);
        contexto.Provider.Verify(
            value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoAdicionarConsultaFalhar_NaoDeveConsultarProvider()
    {
        var contexto = CriarContextoComEnvioAberto();
        contexto.OperacaoRepository
            .Setup(value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), contexto.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("falha simulada de persistência"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        contexto.Provider.Verify(
            value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReconciliarAsync_QuandoProviderCancelarOuFalhar_DeveManterConsultaAberta(bool cancellation)
    {
        var contexto = CriarContextoComEnvioAberto();
        OperacaoPagamentoPix? consultaAdicionada = null;
        contexto.OperacaoRepository
            .Setup(value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), contexto.CancellationToken))
            .Callback<OperacaoPagamentoPix, CancellationToken>((operacao, _) => consultaAdicionada = operacao)
            .Returns(Task.CompletedTask);
        if (cancellation)
        {
            contexto.Provider
                .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken))
                .ThrowsAsync(new OperationCanceledException());
        }
        else
        {
            contexto.Provider
                .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken))
                .ThrowsAsync(new InvalidOperationException("falha simulada do provider"));
        }

        await Assert.ThrowsAnyAsync<Exception>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        Assert.NotNull(consultaAdicionada);
        Assert.False(consultaAdicionada!.FinishedAt.HasValue);
        contexto.OperacaoRepository.Verify(
            value => value.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
        contexto.Provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoFinalizacaoDaConsultaFalhar_NaoDeveConsultarNovamente()
    {
        var contexto = CriarContextoComEnvioAberto();
        contexto.OperacaoRepository
            .Setup(value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), contexto.CancellationToken))
            .Returns(Task.CompletedTask);
        contexto.Provider
            .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken))
            .ReturnsAsync(PixProviderResult.Confirmado());
        contexto.OperacaoRepository
            .Setup(value => value.FinalizarAsync(
                It.Is<OperacaoPagamentoPix>(operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta),
                CancellationToken.None))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        contexto.Provider.Verify(
            value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken),
            Times.Once);
        contexto.Provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReconciliarAsync_QuandoCancelamentoForSolicitadoAposResposta_DeveFinalizarConsultaSemCancelar()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 1);
        var envioAberto = OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1);
        var contexto = CriarContexto(
            [envioAberto],
            pagamento,
            envioAberto,
            cancellationTokenSource.Token);
        contexto.OperacaoRepository
            .Setup(value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), cancellationTokenSource.Token))
            .Returns(Task.CompletedTask);
        contexto.Provider
            .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), cancellationTokenSource.Token))
            .Callback(() => cancellationTokenSource.Cancel())
            .ReturnsAsync(PixProviderResult.Pendente());
        contexto.OperacaoRepository
            .Setup(value => value.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), CancellationToken.None))
            .ReturnsAsync(true);

        var resultado = await contexto.Service.ReconciliarAsync(
            contexto.Pagamento.Id,
            cancellationTokenSource.Token);

        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
        contexto.OperacaoRepository.Verify(
            value => value.FinalizarAsync(
                It.Is<OperacaoPagamentoPix>(operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta),
                CancellationToken.None),
            Times.Once);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado, true)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada, false)]
    public async Task ReconciliarAsync_QuandoFinalizacaoConcorrenteDoEnvioOcorrer_DeveAceitarMesmoResultadoOuRejeitarConflito(
        ResultadoOperacaoPagamentoPix resultadoPersistido,
        bool esperadoBenigno)
    {
        var contexto = CriarContextoComEnvioAberto();
        var operacaoPersistida = OperacaoPagamentoPix.IniciarEnvio(contexto.Pagamento.Id, 1);
        operacaoPersistida.Finalizar(resultadoPersistido);
        contexto.OperacaoRepository
            .Setup(value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), contexto.CancellationToken))
            .Returns(Task.CompletedTask);
        contexto.Provider
            .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken))
            .ReturnsAsync(PixProviderResult.Confirmado());
        contexto.OperacaoRepository
            .Setup(value => value.FinalizarAsync(
                It.Is<OperacaoPagamentoPix>(operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta),
                CancellationToken.None))
            .ReturnsAsync(true);
        contexto.OperacaoRepository
            .Setup(value => value.FinalizarAsync(
                It.Is<OperacaoPagamentoPix>(operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio),
                CancellationToken.None))
            .ReturnsAsync(false);
        contexto.OperacaoRepository
            .Setup(value => value.ObterPorIdAsync(contexto.EnvioAberto!.Id, CancellationToken.None))
            .ReturnsAsync(operacaoPersistida);

        if (esperadoBenigno)
        {
            var resultado = await contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken);
            Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
            Assert.False(resultado.OperacaoEnvioAbertaResolvida);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                contexto.Service.ReconciliarAsync(contexto.Pagamento.Id, contexto.CancellationToken));
        }
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

    private static OperacaoPagamentoPix CriarOperacao(
        Guid pagamentoPixId,
        TipoOperacaoPagamentoPix tipoOperacao,
        int? numeroTentativaEnvio,
        ResultadoOperacaoPagamentoPix? resultado,
        DateTime createdAt) =>
        OperacaoPagamentoPix.Reidratar(
            Guid.NewGuid(),
            pagamentoPixId,
            tipoOperacao,
            numeroTentativaEnvio,
            pagamentoPixId.ToString("N"),
            resultado,
            resultado.HasValue ? "provider-id" : null,
            resultado.HasValue ? "provider-code" : null,
            createdAt,
            resultado.HasValue ? createdAt.AddSeconds(1) : createdAt,
            resultado.HasValue ? createdAt.AddSeconds(1) : null);

    private static void PrepararConsultaComResultado(Contexto contexto, PixProviderResult resultado)
    {
        contexto.OperacaoRepository
            .Setup(value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), contexto.CancellationToken))
            .Returns(Task.CompletedTask);
        contexto.Provider
            .Setup(value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken))
            .ReturnsAsync(resultado);
        contexto.OperacaoRepository
            .Setup(value => value.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), CancellationToken.None))
            .ReturnsAsync(true);
    }

    private static void VerificarSomenteConsulta(Contexto contexto)
    {
        contexto.Provider.Verify(
            value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), contexto.CancellationToken),
            Times.Once);
        contexto.Provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static void VerificarNenhumaConsultaNova(Contexto contexto)
    {
        contexto.Provider.Verify(
            value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        contexto.Provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        contexto.OperacaoRepository.Verify(
            value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Contexto CriarContextoComEnvioAberto()
    {
        var pagamento = CriarPagamento(StatusPagamentoPix.Processando, 1);
        var envioAberto = OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1);
        return CriarContexto([envioAberto], pagamento, envioAberto);
    }

    private static Contexto CriarContexto(
        IEnumerable<OperacaoPagamentoPix> historico,
        PagamentoPix? pagamento = null,
        OperacaoPagamentoPix? envioAberto = null,
        CancellationToken cancellationToken = default)
    {
        pagamento ??= CriarPagamento(StatusPagamentoPix.Processando, 1);
        if (cancellationToken == default)
            cancellationToken = new CancellationTokenSource().Token;
        var operacoes = historico.ToList();
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        var operacaoRepository = new Mock<IOperacaoPagamentoPixRepository>();
        var provider = new Mock<IPixProvider>();
        pagamentoRepository
            .Setup(value => value.ObterPorIdAsync(pagamento.Id, cancellationToken))
            .ReturnsAsync(pagamento);
        operacaoRepository
            .Setup(value => value.ObterPorPagamentoPixIdAsync(pagamento.Id, cancellationToken))
            .ReturnsAsync(operacoes);

        return new Contexto(
            CriarService(pagamentoRepository, operacaoRepository, provider),
            pagamento,
            envioAberto,
            operacoes,
            pagamentoRepository,
            operacaoRepository,
            provider,
            cancellationToken);
    }

    private static PagamentoPixReconciliacaoService CriarService(
        Mock<IPagamentoPixRepository> pagamentoRepository,
        Mock<IOperacaoPagamentoPixRepository> operacaoRepository,
        Mock<IPixProvider> provider) =>
        new(pagamentoRepository.Object, operacaoRepository.Object, provider.Object);

    private static PagamentoPix CriarPagamento(StatusPagamentoPix status, int quantidadeTentativas)
    {
        var pagamento = PagamentoPix.Criar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            123.45m,
            TipoChavePix.Email,
            "beneficiario@exemplo.com");
        return status == StatusPagamentoPix.Pendente
            ? pagamento
            : PagamentoPix.Reidratar(
                pagamento.Id,
                pagamento.CashbackId,
                pagamento.UsuarioBeneficiarioId,
                pagamento.Valor,
                pagamento.TipoChavePix,
                pagamento.ChavePix,
                status,
                quantidadeTentativas,
                pagamento.CreatedAt,
                pagamento.UpdatedAt);
    }

    private static void VerificarNenhumaChamadaExternaOuMutacao(
        Mock<IPagamentoPixRepository> pagamentoRepository,
        Mock<IPixProvider> provider)
    {
        provider.Verify(
            value => value.ConsultarAsync(It.IsAny<PixConsultaRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        provider.Verify(
            value => value.EnviarAsync(It.IsAny<PixEnvioRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerificarNenhumaMutacaoPagamento(pagamentoRepository);
    }

    private static void VerificarNenhumaMutacaoPagamento(Mock<IPagamentoPixRepository> pagamentoRepository)
    {
        pagamentoRepository.Verify(
            value => value.AtualizarAsync(It.IsAny<PagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
        pagamentoRepository.Verify(
            value => value.TentarIniciarProcessamentoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed record Contexto(
        PagamentoPixReconciliacaoService Service,
        PagamentoPix Pagamento,
        OperacaoPagamentoPix? EnvioAberto,
        List<OperacaoPagamentoPix> Historico,
        Mock<IPagamentoPixRepository> PagamentoRepository,
        Mock<IOperacaoPagamentoPixRepository> OperacaoRepository,
        Mock<IPixProvider> Provider,
        CancellationToken CancellationToken);
}

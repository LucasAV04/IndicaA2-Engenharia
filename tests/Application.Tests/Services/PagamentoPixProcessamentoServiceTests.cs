using Application.Interfaces.Services;
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

public sealed class PagamentoPixProcessamentoServiceTests
{
    [Fact]
    public async Task ProcessarAsync_QuandoIdentificadorForVazio_DeveRejeitarSemAcessarDependencias()
    {
        var contexto = CriarContexto(StatusPagamentoPix.Pendente);

        await Assert.ThrowsAsync<ArgumentException>(() => contexto.Service.ProcessarAsync(Guid.Empty));

        contexto.Pagamentos.Verify(x => x.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerificarNenhumServicoEspecializadoFoiChamado(contexto);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoPagamentoNaoExistir_DeveLancarExcecaoEspecifica()
    {
        var pagamentos = new Mock<IPagamentoPixRepository>();
        pagamentos.Setup(x => x.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoPix?)null);
        var contexto = CriarContexto(StatusPagamentoPix.Pendente, pagamentos);

        await Assert.ThrowsAsync<PagamentoPixNaoEncontradoException>(() =>
            contexto.Service.ProcessarAsync(Guid.NewGuid()));

        VerificarNenhumServicoEspecializadoFoiChamado(contexto);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoPendenteEEnvioConfirmado_DeveAplicarSemReconcilia()
    {
        var contexto = CriarContexto(StatusPagamentoPix.Pendente);
        contexto.Envio.Setup(x => x.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoEnvioPagamentoPix.Executado(
                contexto.Pagamento.Id,
                Guid.NewGuid(),
                1,
                ResultadoOperacaoPagamentoPix.Confirmado));
        contexto.Aplicacao.Setup(x => x.AplicarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoAplicacaoPagamentoPix.Aplicado(
                contexto.Pagamento.Id,
                ResultadoOperacaoPagamentoPix.Confirmado));

        var resultado = await contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusProcessamentoPagamentoPix.Aplicado, resultado.Status);
        contexto.Envio.Verify(x => x.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
        contexto.Aplicacao.Verify(x => x.AplicarAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
        contexto.Reconciliacao.Verify(x => x.ReconciliarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Pendente)]
    [InlineData(ResultadoOperacaoPagamentoPix.Indeterminado)]
    public async Task ProcessarAsync_QuandoPendenteEEnvioNaoConclusivo_DeveAguardarSemConsultar(
        ResultadoOperacaoPagamentoPix resultadoProvider)
    {
        var contexto = CriarContexto(StatusPagamentoPix.Pendente);
        contexto.Envio.Setup(x => x.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoEnvioPagamentoPix.Executado(contexto.Pagamento.Id, Guid.NewGuid(), 1, resultadoProvider));

        var resultado = await contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusProcessamentoPagamentoPix.EnvioExecutadoAguardandoResultado, resultado.Status);
        contexto.Aplicacao.Verify(x => x.AplicarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        contexto.Reconciliacao.Verify(x => x.ReconciliarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoProcessandoPossuirEvidenciaConclusiva_DeveAplicarSemProvider()
    {
        var contexto = CriarContexto(StatusPagamentoPix.Processando);
        contexto.Aplicacao.Setup(x => x.AplicarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoAplicacaoPagamentoPix.Aplicado(
                contexto.Pagamento.Id,
                ResultadoOperacaoPagamentoPix.Confirmado));

        var resultado = await contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusProcessamentoPagamentoPix.Aplicado, resultado.Status);
        contexto.Envio.Verify(x => x.ProcessarEnvioAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        contexto.Reconciliacao.Verify(x => x.ReconciliarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoProcessandoSemEvidencia_DeveReconciliarUmaVez()
    {
        var contexto = CriarContexto(StatusPagamentoPix.Processando);
        ConfigurarSemEvidencia(contexto);
        contexto.Reconciliacao.Setup(x => x.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoReconciliacaoPagamentoPix.Consultado(
                contexto.Pagamento.Id,
                Guid.NewGuid(),
                ResultadoOperacaoPagamentoPix.Pendente,
                false));

        var resultado = await contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusProcessamentoPagamentoPix.ReconciliacaoExecutadaAguardandoResultado, resultado.Status);
        contexto.Reconciliacao.Verify(x => x.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
        contexto.Envio.Verify(x => x.ProcessarEnvioAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoReconciliacaoForConclusiva_DeveAplicarUmaSegundaVez()
    {
        var contexto = CriarContexto(StatusPagamentoPix.Processando);
        contexto.Aplicacao.SetupSequence(x => x.AplicarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoAplicacaoPagamentoPix.SemResultadoConclusivo(contexto.Pagamento.Id))
            .ReturnsAsync(ResultadoAplicacaoPagamentoPix.Aplicado(
                contexto.Pagamento.Id,
                ResultadoOperacaoPagamentoPix.Confirmado));
        contexto.Reconciliacao.Setup(x => x.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoReconciliacaoPagamentoPix.Consultado(
                contexto.Pagamento.Id,
                Guid.NewGuid(),
                ResultadoOperacaoPagamentoPix.Confirmado,
                false));

        var resultado = await contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusProcessamentoPagamentoPix.Aplicado, resultado.Status);
        contexto.Aplicacao.Verify(x => x.AplicarAsync(contexto.Pagamento.Id, contexto.Token), Times.Exactly(2));
        contexto.Reconciliacao.Verify(x => x.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
    }

    [Theory]
    [InlineData(StatusReconciliacaoPagamentoPix.ConsultaEmAndamento, StatusProcessamentoPagamentoPix.ConsultaEmAndamento)]
    [InlineData(StatusReconciliacaoPagamentoPix.EnvioEmAndamento, StatusProcessamentoPagamentoPix.EnvioEmAndamento)]
    [InlineData(StatusReconciliacaoPagamentoPix.EnvioPendenteRecuperacao, StatusProcessamentoPagamentoPix.EnvioPendenteRecuperacao)]
    public async Task ProcessarAsync_QuandoReconciliacaoEstiverAguardando_DeveRetornarEsperaSemNovaChamada(
        StatusReconciliacaoPagamentoPix statusReconciliacao,
        StatusProcessamentoPagamentoPix statusEsperado)
    {
        var contexto = CriarContexto(StatusPagamentoPix.Processando);
        ConfigurarSemEvidencia(contexto);
        contexto.Reconciliacao.Setup(x => x.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(CriarResultadoEspera(contexto.Pagamento.Id, statusReconciliacao));

        var resultado = await contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(statusEsperado, resultado.Status);
        contexto.Aplicacao.Verify(x => x.AplicarAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
        contexto.Reconciliacao.Verify(x => x.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
        contexto.Envio.Verify(x => x.ProcessarEnvioAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoFalhou_DeveAguardarPoliticaSemIniciarRetry()
    {
        var contexto = CriarContexto(StatusPagamentoPix.Falhou);

        var resultado = await contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(StatusProcessamentoPagamentoPix.AguardandoPoliticaRetry, resultado.Status);
        VerificarNenhumServicoEspecializadoFoiChamado(contexto);
    }

    [Theory]
    [InlineData(StatusPagamentoPix.Concluido, StatusProcessamentoPagamentoPix.Terminal)]
    [InlineData(StatusPagamentoPix.FalhaDefinitiva, StatusProcessamentoPagamentoPix.Terminal)]
    [InlineData(StatusPagamentoPix.Cancelado, StatusProcessamentoPagamentoPix.NaoAplicavel)]
    public async Task ProcessarAsync_QuandoTerminal_NaoDeveAcionarFluxosInternos(
        StatusPagamentoPix statusPagamento,
        StatusProcessamentoPagamentoPix statusEsperado)
    {
        var contexto = CriarContexto(statusPagamento);

        var resultado = await contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token);

        Assert.Equal(statusEsperado, resultado.Status);
        VerificarNenhumServicoEspecializadoFoiChamado(contexto);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoEnvioFalhar_DevePropagarSemRetry()
    {
        var contexto = CriarContexto(StatusPagamentoPix.Pendente);
        contexto.Envio.Setup(x => x.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token))
            .ThrowsAsync(new InvalidOperationException("falha de envio"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token));

        contexto.Envio.Verify(x => x.ProcessarEnvioAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
        contexto.Reconciliacao.Verify(x => x.ReconciliarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoReconciliacaoFalhar_DevePropagarSemRetry()
    {
        var contexto = CriarContexto(StatusPagamentoPix.Processando);
        ConfigurarSemEvidencia(contexto);
        contexto.Reconciliacao.Setup(x => x.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token))
            .ThrowsAsync(new InvalidOperationException("falha de reconciliação"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.ProcessarAsync(contexto.Pagamento.Id, contexto.Token));

        contexto.Reconciliacao.Verify(x => x.ReconciliarAsync(contexto.Pagamento.Id, contexto.Token), Times.Once);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoCancelado_DeveRespeitarTokenSemChamarDependencias()
    {
        var contexto = CriarContexto(StatusPagamentoPix.Pendente);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            contexto.Service.ProcessarAsync(contexto.Pagamento.Id, source.Token));

        contexto.Pagamentos.Verify(x => x.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerificarNenhumServicoEspecializadoFoiChamado(contexto);
    }

    [Fact]
    public void Resultado_NaoDeveExporDadosSensiveis()
    {
        var propriedades = typeof(ResultadoProcessamentoPagamentoPix)
            .GetProperties()
            .Select(x => x.Name);

        Assert.DoesNotContain(propriedades, nome => nome.Contains("ChavePix", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propriedades, nome => nome.Contains("Lease", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propriedades, nome => nome.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propriedades, nome => nome.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Servico_NaoDeveDependerDiretamenteDeProviderOuStores()
    {
        var dependencias = typeof(PagamentoPixProcessamentoService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType);

        Assert.DoesNotContain(typeof(IPixProvider), dependencias);
        Assert.DoesNotContain(typeof(IPagamentoPixEnvioStore), dependencias);
        Assert.DoesNotContain(typeof(IPagamentoPixReconciliacaoStore), dependencias);
        Assert.DoesNotContain(typeof(IPagamentoPixAplicacaoResultadoStore), dependencias);
    }

    private static void ConfigurarSemEvidencia(Contexto contexto) =>
        contexto.Aplicacao.Setup(x => x.AplicarAsync(contexto.Pagamento.Id, contexto.Token))
            .ReturnsAsync(ResultadoAplicacaoPagamentoPix.SemResultadoConclusivo(contexto.Pagamento.Id));

    private static ResultadoReconciliacaoPagamentoPix CriarResultadoEspera(
        Guid pagamentoPixId,
        StatusReconciliacaoPagamentoPix status) =>
        status switch
        {
            StatusReconciliacaoPagamentoPix.ConsultaEmAndamento => ResultadoReconciliacaoPagamentoPix.ConsultaEmAndamento(pagamentoPixId),
            StatusReconciliacaoPagamentoPix.EnvioEmAndamento => ResultadoReconciliacaoPagamentoPix.EnvioEmAndamento(pagamentoPixId),
            StatusReconciliacaoPagamentoPix.EnvioPendenteRecuperacao => ResultadoReconciliacaoPagamentoPix.EnvioPendenteRecuperacao(pagamentoPixId),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static Contexto CriarContexto(
        StatusPagamentoPix status,
        Mock<IPagamentoPixRepository>? pagamentos = null)
    {
        var pagamento = CriarPagamento(status);
        pagamentos ??= new Mock<IPagamentoPixRepository>();
        pagamentos.Setup(x => x.ObterPorIdAsync(pagamento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);
        var envio = new Mock<IPagamentoPixEnvioService>();
        var reconciliacao = new Mock<IPagamentoPixReconciliacaoService>();
        var aplicacao = new Mock<IPagamentoPixAplicacaoResultadoService>();

        return new Contexto(
            pagamento,
            pagamentos,
            envio,
            reconciliacao,
            aplicacao,
            new PagamentoPixProcessamentoService(
                pagamentos.Object,
                envio.Object,
                reconciliacao.Object,
                aplicacao.Object),
            CancellationToken.None);
    }

    private static PagamentoPix CriarPagamento(StatusPagamentoPix status)
    {
        var tentativas = status switch
        {
            StatusPagamentoPix.Pendente or StatusPagamentoPix.Cancelado => 0,
            StatusPagamentoPix.FalhaDefinitiva => PagamentoPix.TentativasMaximas,
            _ => 1
        };
        var agora = DateTime.UtcNow;
        return PagamentoPix.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            TipoChavePix.Email,
            "beneficiario@example.test",
            status,
            tentativas,
            agora.AddMinutes(-1),
            agora);
    }

    private static void VerificarNenhumServicoEspecializadoFoiChamado(Contexto contexto)
    {
        contexto.Envio.Verify(x => x.ProcessarEnvioAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        contexto.Reconciliacao.Verify(x => x.ReconciliarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        contexto.Aplicacao.Verify(x => x.AplicarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed record Contexto(
        PagamentoPix Pagamento,
        Mock<IPagamentoPixRepository> Pagamentos,
        Mock<IPagamentoPixEnvioService> Envio,
        Mock<IPagamentoPixReconciliacaoService> Reconciliacao,
        Mock<IPagamentoPixAplicacaoResultadoService> Aplicacao,
        PagamentoPixProcessamentoService Service,
        CancellationToken Token);
}

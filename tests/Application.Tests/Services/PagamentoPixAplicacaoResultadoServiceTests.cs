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
    public async Task AplicarAsync_QuandoIdentificadorForVazio_DeveRejeitar()
    {
        var contexto = CriarContexto();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            contexto.Service.AplicarAsync(Guid.Empty, contexto.CancellationToken));
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
            Mock.Of<IOperacaoPagamentoPixRepository>(),
            Mock.Of<IPagamentoPixAplicacaoResultadoStore>());

        await Assert.ThrowsAsync<PagamentoPixNaoEncontradoException>(() =>
            service.AplicarAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Pendente)]
    [InlineData(ResultadoOperacaoPagamentoPix.Indeterminado)]
    public async Task AplicarAsync_QuandoCicloNaoTiverResultadoConclusivo_DeveRetornarSemResultado(
        ResultadoOperacaoPagamentoPix resultado)
    {
        var contexto = CriarContextoComCicloAtual(resultado, envioAberto: false);

        var retorno = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusAplicacaoPagamentoPix.SemResultadoConclusivo, retorno.Status);
        VerificarNenhumaMutacao(contexto);
    }

    [Fact]
    public async Task AplicarAsync_QuandoEnvioAtualEstiverAusente_DeveFalharFechado()
    {
        var contexto = CriarContexto(historico: []);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        VerificarNenhumaMutacao(contexto);
    }

    [Fact]
    public async Task AplicarAsync_QuandoEnvioAtualEstiverDuplicado_DeveFalharFechado()
    {
        var contexto = CriarContexto();
        contexto.Historico.Add(CriarOperacao(
            contexto.Pagamento.Id,
            TipoOperacaoPagamentoPix.Envio,
            1,
            ResultadoOperacaoPagamentoPix.Confirmado,
            DateTime.UtcNow.AddMinutes(-1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        VerificarNenhumaMutacao(contexto);
    }

    [Fact]
    public async Task AplicarAsync_QuandoEnvioAnteriorEstiverAberto_DeveFalharFechado()
    {
        var contexto = CriarContexto(tentativas: 2);
        contexto.Historico.Add(CriarOperacao(
            contexto.Pagamento.Id,
            TipoOperacaoPagamentoPix.Envio,
            1,
            null,
            DateTime.UtcNow.AddMinutes(-2)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        VerificarNenhumaMutacao(contexto);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    public async Task AplicarAsync_QuandoResultadoConclusivoForDeTentativaAnterior_DeveIgnoraLo(
        ResultadoOperacaoPagamentoPix resultadoAnterior)
    {
        var contexto = CriarContexto(tentativas: 2);
        contexto.Historico.Add(CriarOperacao(
            contexto.Pagamento.Id,
            TipoOperacaoPagamentoPix.Envio,
            1,
            resultadoAnterior,
            DateTime.UtcNow.AddMinutes(-2)));

        var retorno = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusAplicacaoPagamentoPix.SemResultadoConclusivo, retorno.Status);
        VerificarNenhumaMutacao(contexto);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    public async Task AplicarAsync_QuandoConsultaConclusivaDoCicloAtualExistirEEnvioEstiverAberto_DeveRequererReconciliacao(
        ResultadoOperacaoPagamentoPix resultado)
    {
        var contexto = CriarContextoComCicloAtual(resultado, envioAberto: true);

        var retorno = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusAplicacaoPagamentoPix.RequerReconciliacao, retorno.Status);
        VerificarNenhumaMutacao(contexto);
    }

    [Fact]
    public async Task AplicarAsync_QuandoCicloAtualPossuirResultadosConclusivosConflitantes_DeveFalharFechado()
    {
        var contexto = CriarContextoComCicloAtual(ResultadoOperacaoPagamentoPix.Confirmado, envioAberto: false);
        contexto.Historico.Add(CriarOperacao(
            contexto.Pagamento.Id,
            TipoOperacaoPagamentoPix.Consulta,
            null,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada,
            DateTime.UtcNow.AddMinutes(1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        VerificarNenhumaMutacao(contexto);
    }

    [Fact]
    public async Task AplicarAsync_QuandoConfirmado_DeveCoordenarConclusaoEPagamento()
    {
        var contexto = CriarContextoComCicloAtual(ResultadoOperacaoPagamentoPix.Confirmado, envioAberto: false);
        AplicacaoResultadoPagamentoPixRequest? request = null;
        contexto.Store
            .Setup(value => value.AplicarAsync(It.IsAny<AplicacaoResultadoPagamentoPixRequest>(), contexto.CancellationToken))
            .Callback<AplicacaoResultadoPagamentoPixRequest, CancellationToken>((valor, _) => request = valor)
            .ReturnsAsync(ResultadoPersistenciaAplicacaoPagamentoPix.Aplicado);

        var retorno = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusAplicacaoPagamentoPix.Aplicado, retorno.Status);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, retorno.ResultadoOperacao);
        Assert.Equal(StatusPagamentoPix.Concluido, contexto.Pagamento.Status);
        Assert.Equal(StatusCashback.Pago, contexto.Cashback.Status);
        Assert.NotNull(request);
        Assert.Equal(StatusPagamentoPix.Concluido, request!.StatusPagamentoPixFinal);
        Assert.Equal(StatusCashback.Pago, request.StatusCashbackFinal);
        contexto.Store.Verify(
            value => value.AplicarAsync(It.IsAny<AplicacaoResultadoPagamentoPixRequest>(), contexto.CancellationToken),
            Times.Once);
        VerificarAuditoriaImutavel(contexto);
    }

    [Theory]
    [InlineData(1, StatusPagamentoPix.Falhou)]
    [InlineData(4, StatusPagamentoPix.Falhou)]
    [InlineData(5, StatusPagamentoPix.FalhaDefinitiva)]
    public async Task AplicarAsync_QuandoFalhaConfirmada_DeveRegistrarFalhaSemAlterarCashback(
        int tentativas,
        StatusPagamentoPix statusEsperado)
    {
        var contexto = CriarContextoComCicloAtual(
            ResultadoOperacaoPagamentoPix.FalhaConfirmada,
            envioAberto: false,
            tentativas: tentativas);
        contexto.Store
            .Setup(value => value.AplicarAsync(It.IsAny<AplicacaoResultadoPagamentoPixRequest>(), contexto.CancellationToken))
            .ReturnsAsync(ResultadoPersistenciaAplicacaoPagamentoPix.Aplicado);

        var retorno = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusAplicacaoPagamentoPix.Aplicado, retorno.Status);
        Assert.Equal(statusEsperado, contexto.Pagamento.Status);
        Assert.Equal(StatusCashback.Disponivel, contexto.Cashback.Status);
        contexto.Store.Verify(
            value => value.AplicarAsync(
                It.Is<AplicacaoResultadoPagamentoPixRequest>(request =>
                    request.StatusPagamentoPixFinal == statusEsperado &&
                    request.StatusCashbackFinal == StatusCashback.Disponivel),
                contexto.CancellationToken),
            Times.Once);
        VerificarAuditoriaImutavel(contexto);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    public async Task AplicarAsync_QuandoStoreIndicarAplicacaoConcorrenteJaConcluida_DeveSerIdempotente(
        ResultadoOperacaoPagamentoPix resultado)
    {
        var contexto = CriarContextoComCicloAtual(resultado, envioAberto: false);
        contexto.Store
            .Setup(value => value.AplicarAsync(It.IsAny<AplicacaoResultadoPagamentoPixRequest>(), contexto.CancellationToken))
            .ReturnsAsync(ResultadoPersistenciaAplicacaoPagamentoPix.JaAplicado);

        var retorno = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusAplicacaoPagamentoPix.JaAplicado, retorno.Status);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado, StatusPagamentoPix.Concluido, StatusCashback.Pago)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada, StatusPagamentoPix.Falhou, StatusCashback.Disponivel)]
    public async Task AplicarAsync_QuandoResultadoJaEstiverPersistidoDeFormaCoerente_DeveSerIdempotente(
        ResultadoOperacaoPagamentoPix resultado,
        StatusPagamentoPix statusPagamento,
        StatusCashback statusCashback)
    {
        var contexto = CriarContextoComCicloAtual(
            resultado,
            envioAberto: false,
            statusPagamento: statusPagamento,
            statusCashback: statusCashback);

        var retorno = await contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken);

        Assert.Equal(StatusAplicacaoPagamentoPix.JaAplicado, retorno.Status);
        contexto.Store.Verify(
            value => value.AplicarAsync(It.IsAny<AplicacaoResultadoPagamentoPixRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerificarAuditoriaImutavel(contexto);
    }

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado, StatusPagamentoPix.Falhou, StatusCashback.Disponivel)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada, StatusPagamentoPix.Concluido, StatusCashback.Pago)]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado, StatusPagamentoPix.Processando, StatusCashback.Pago)]
    public async Task AplicarAsync_QuandoEstadoFinanceiroForParcialOuIncompativel_DeveFalharFechado(
        ResultadoOperacaoPagamentoPix resultado,
        StatusPagamentoPix statusPagamento,
        StatusCashback statusCashback)
    {
        var contexto = CriarContextoComCicloAtual(
            resultado,
            envioAberto: false,
            statusPagamento: statusPagamento,
            statusCashback: statusCashback);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken));

        contexto.Store.Verify(
            value => value.AplicarAsync(It.IsAny<AplicacaoResultadoPagamentoPixRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task AplicarAsync_QuandoSnapshotsFinanceirosDivergirem_DeveFalharFechado(
        bool cashbackDivergente,
        bool beneficiarioDivergente,
        bool valorDivergente)
    {
        var contexto = CriarContextoComCicloAtual(ResultadoOperacaoPagamentoPix.Confirmado, envioAberto: false);
        var pagamentoOriginal = contexto.Pagamento;
        var cashbackId = cashbackDivergente ? Guid.NewGuid() : contexto.Cashback.Id;
        contexto.Pagamento = PagamentoPix.Reidratar(
            pagamentoOriginal.Id,
            cashbackId,
            beneficiarioDivergente ? Guid.NewGuid() : contexto.Cashback.UsuarioIndicadorId,
            valorDivergente ? contexto.Cashback.Valor + 1m : contexto.Cashback.Valor,
            TipoChavePix.Email,
            "snapshot@exemplo.com",
            StatusPagamentoPix.Processando,
            1,
            pagamentoOriginal.CreatedAt,
            pagamentoOriginal.UpdatedAt);
        contexto.PagamentoRepository
            .Setup(value => value.ObterPorIdAsync(contexto.Pagamento.Id, contexto.CancellationToken))
            .ReturnsAsync(contexto.Pagamento);
        contexto.CashbackRepository
            .Setup(value => value.ObterPorIdAsync(cashbackId, contexto.CancellationToken))
            .ReturnsAsync(contexto.Cashback);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexto.Service.AplicarAsync(contexto.Pagamento.Id, contexto.CancellationToken));
    }

    [Fact]
    public void Servico_NaoDeveDependerDeIPixProvider()
    {
        var dependencias = typeof(PagamentoPixAplicacaoResultadoService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(IPixProvider), dependencias);
    }

    private static Contexto CriarContextoComCicloAtual(
        ResultadoOperacaoPagamentoPix resultado,
        bool envioAberto,
        int tentativas = 1,
        StatusPagamentoPix statusPagamento = StatusPagamentoPix.Processando,
        StatusCashback statusCashback = StatusCashback.Disponivel)
    {
        var contexto = CriarContexto(tentativas, statusPagamento, statusCashback);
        var inicio = DateTime.UtcNow.AddMinutes(-2);
        contexto.Historico.Clear();
        contexto.Historico.Add(CriarOperacao(
            contexto.Pagamento.Id,
            TipoOperacaoPagamentoPix.Envio,
            tentativas,
            envioAberto ? null : ResultadoOperacaoPagamentoPix.Pendente,
            inicio));
        contexto.Historico.Add(CriarOperacao(
            contexto.Pagamento.Id,
            TipoOperacaoPagamentoPix.Consulta,
            null,
            resultado,
            inicio.AddMinutes(1)));
        return contexto;
    }

    private static Contexto CriarContexto(
        int tentativas = 1,
        StatusPagamentoPix statusPagamento = StatusPagamentoPix.Processando,
        StatusCashback statusCashback = StatusCashback.Disponivel,
        IReadOnlyCollection<OperacaoPagamentoPix>? historico = null)
    {
        var cashback = CriarCashback(statusCashback);
        var pagamento = CriarPagamento(cashback, statusPagamento, tentativas);
        var token = new CancellationTokenSource().Token;
        var pagamentoRepository = new Mock<IPagamentoPixRepository>();
        var cashbackRepository = new Mock<ICashbackRepository>();
        var operacaoRepository = new Mock<IOperacaoPagamentoPixRepository>();
        var store = new Mock<IPagamentoPixAplicacaoResultadoStore>();
        var operacoes = historico?.ToList() ??
            [CriarOperacao(pagamento.Id, TipoOperacaoPagamentoPix.Envio, tentativas, ResultadoOperacaoPagamentoPix.Pendente, DateTime.UtcNow.AddMinutes(-2))];

        pagamentoRepository
            .Setup(value => value.ObterPorIdAsync(pagamento.Id, token))
            .ReturnsAsync(pagamento);
        cashbackRepository
            .Setup(value => value.ObterPorIdAsync(cashback.Id, token))
            .ReturnsAsync(cashback);
        operacaoRepository
            .Setup(value => value.ObterPorPagamentoPixIdAsync(pagamento.Id, token))
            .ReturnsAsync(operacoes);

        return new Contexto(
            new PagamentoPixAplicacaoResultadoService(
                pagamentoRepository.Object,
                cashbackRepository.Object,
                operacaoRepository.Object,
                store.Object),
            pagamento,
            cashback,
            operacoes,
            pagamentoRepository,
            cashbackRepository,
            operacaoRepository,
            store,
            token);
    }

    private static Cashback CriarCashback(StatusCashback status)
    {
        var cashback = Cashback.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m);
        if (status is StatusCashback.Disponivel or StatusCashback.Pago)
            cashback.Aprovar();
        if (status == StatusCashback.Pago)
            cashback.RegistrarPagamento();
        if (status == StatusCashback.Cancelado)
            cashback.Cancelar();
        return cashback;
    }

    private static PagamentoPix CriarPagamento(Cashback cashback, StatusPagamentoPix status, int tentativas)
    {
        var pagamento = PagamentoPix.Criar(
            cashback.Id,
            cashback.UsuarioIndicadorId,
            cashback.Valor,
            TipoChavePix.Email,
            "snapshot@exemplo.com");
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
                tentativas,
                pagamento.CreatedAt,
                pagamento.UpdatedAt);
    }

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

    private static void VerificarNenhumaMutacao(Contexto contexto)
    {
        contexto.Store.Verify(
            value => value.AplicarAsync(It.IsAny<AplicacaoResultadoPagamentoPixRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerificarAuditoriaImutavel(contexto);
    }

    private static void VerificarAuditoriaImutavel(Contexto contexto)
    {
        contexto.OperacaoRepository.Verify(
            value => value.AdicionarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
        contexto.OperacaoRepository.Verify(
            value => value.FinalizarAsync(It.IsAny<OperacaoPagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class Contexto(
        PagamentoPixAplicacaoResultadoService service,
        PagamentoPix pagamento,
        Cashback cashback,
        List<OperacaoPagamentoPix> historico,
        Mock<IPagamentoPixRepository> pagamentoRepository,
        Mock<ICashbackRepository> cashbackRepository,
        Mock<IOperacaoPagamentoPixRepository> operacaoRepository,
        Mock<IPagamentoPixAplicacaoResultadoStore> store,
        CancellationToken cancellationToken)
    {
        public PagamentoPixAplicacaoResultadoService Service { get; } = service;
        public PagamentoPix Pagamento { get; set; } = pagamento;
        public Cashback Cashback { get; } = cashback;
        public List<OperacaoPagamentoPix> Historico { get; } = historico;
        public Mock<IPagamentoPixRepository> PagamentoRepository { get; } = pagamentoRepository;
        public Mock<ICashbackRepository> CashbackRepository { get; } = cashbackRepository;
        public Mock<IOperacaoPagamentoPixRepository> OperacaoRepository { get; } = operacaoRepository;
        public Mock<IPagamentoPixAplicacaoResultadoStore> Store { get; } = store;
        public CancellationToken CancellationToken { get; } = cancellationToken;
    }
}

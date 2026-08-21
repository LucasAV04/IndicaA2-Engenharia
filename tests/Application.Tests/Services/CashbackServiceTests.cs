using Application.Interfaces.Services;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class CashbackServiceTests
{
    [Fact]
    public async Task GerarPorPagamentoAsync_QuandoPagamentoConfirmado_DeveRastrearIndicacaoERegistrarCashback()
    {
        var cashbackRepository = new Mock<ICashbackRepository>();
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        var pagamento = CriarPagamentoConfirmado(499.90m);
        var indicadorId = Guid.NewGuid();
        var indicacao = CriarIndicacaoVinculada(pagamento.VistoriaId, indicadorId);
        var cancellationToken = new CancellationTokenSource().Token;
        Cashback? persistido = null;
        pagamentoRepository
            .Setup(repository => repository.ObterPorIdAsync(pagamento.Id, cancellationToken))
            .ReturnsAsync(pagamento);
        indicacaoRepository
            .Setup(repository => repository.ObterPorVistoriaIdAsync(pagamento.VistoriaId, cancellationToken))
            .ReturnsAsync(indicacao);
        cashbackRepository
            .Setup(repository => repository.ObterPorPagamentoVistoriaIdAsync(pagamento.Id, cancellationToken))
            .ReturnsAsync((Cashback?)null);
        cashbackRepository
            .Setup(repository => repository.AdicionarAsync(It.IsAny<Cashback>(), cancellationToken))
            .Callback<Cashback, CancellationToken>((cashback, _) => persistido = cashback)
            .Returns(Task.CompletedTask);

        var resultado = await CriarService(cashbackRepository, indicacaoRepository, pagamentoRepository)
            .GerarPorPagamentoAsync(pagamento.Id, cancellationToken);

        Assert.NotNull(persistido);
        Assert.Equal(indicacao.Id, resultado.IndicacaoId);
        Assert.Equal(pagamento.Id, resultado.PagamentoVistoriaId);
        Assert.Equal(indicadorId, resultado.UsuarioIndicadorId);
        Assert.Equal(pagamento.Valor, resultado.ValorTotalPago);
        Assert.Equal(0.20m, resultado.Percentual);
        Assert.Equal(99.98m, resultado.Valor);
        Assert.Equal(StatusCashback.Pendente, resultado.Status);
        indicacaoRepository.Verify(
            repository => repository.ObterPorVistoriaIdAsync(pagamento.VistoriaId, cancellationToken),
            Times.Once);
        cashbackRepository.Verify(
            repository => repository.AdicionarAsync(It.Is<Cashback>(cashback =>
                cashback.UsuarioIndicadorId == indicadorId &&
                cashback.ValorTotalPago == pagamento.Valor &&
                cashback.Valor == 99.98m), cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task GerarPorPagamentoAsync_QuandoIdForVazio_NaoDeveConsultarRepositories()
    {
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CriarService(new Mock<ICashbackRepository>(), new Mock<IIndicacaoRepository>(), pagamentoRepository)
                .GerarPorPagamentoAsync(Guid.Empty));

        pagamentoRepository.Verify(
            repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GerarPorPagamentoAsync_QuandoPagamentoNaoExistir_DeveLancarExcecaoEspecifica()
    {
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        var pagamentoId = Guid.NewGuid();
        pagamentoRepository
            .Setup(repository => repository.ObterPorIdAsync(pagamentoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoVistoria?)null);

        await Assert.ThrowsAsync<PagamentoVistoriaNaoEncontradoException>(() =>
            CriarService(new Mock<ICashbackRepository>(), new Mock<IIndicacaoRepository>(), pagamentoRepository)
                .GerarPorPagamentoAsync(pagamentoId));
    }

    [Theory]
    [InlineData(StatusPagamentoVistoria.Pendente)]
    [InlineData(StatusPagamentoVistoria.Cancelado)]
    public async Task GerarPorPagamentoAsync_QuandoPagamentoNaoConfirmado_NaoDeveGerarCashback(
        StatusPagamentoVistoria status)
    {
        var cashbackRepository = new Mock<ICashbackRepository>();
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        var pagamento = new PagamentoVistoria(Guid.NewGuid(), 500m);
        if (status == StatusPagamentoVistoria.Cancelado)
            pagamento.Cancelar();
        pagamentoRepository
            .Setup(repository => repository.ObterPorIdAsync(pagamento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);

        await Assert.ThrowsAsync<DomainException>(() =>
            CriarService(cashbackRepository, indicacaoRepository, pagamentoRepository)
                .GerarPorPagamentoAsync(pagamento.Id));

        indicacaoRepository.Verify(
            repository => repository.ObterPorVistoriaIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        cashbackRepository.Verify(
            repository => repository.AdicionarAsync(It.IsAny<Cashback>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GerarPorPagamentoAsync_QuandoIndicacaoNaoExistir_NaoDeveGerarCashback()
    {
        var cashbackRepository = new Mock<ICashbackRepository>();
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        var pagamento = CriarPagamentoConfirmado();
        pagamentoRepository
            .Setup(repository => repository.ObterPorIdAsync(pagamento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);
        indicacaoRepository
            .Setup(repository => repository.ObterPorVistoriaIdAsync(pagamento.VistoriaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Indicacao?)null);

        await Assert.ThrowsAsync<DomainException>(() =>
            CriarService(cashbackRepository, indicacaoRepository, pagamentoRepository)
                .GerarPorPagamentoAsync(pagamento.Id));

        cashbackRepository.Verify(
            repository => repository.AdicionarAsync(It.IsAny<Cashback>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GerarPorPagamentoAsync_QuandoCashbackJaExistir_DeveImpedirDuplicidade()
    {
        var cashbackRepository = new Mock<ICashbackRepository>();
        var indicacaoRepository = new Mock<IIndicacaoRepository>();
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        var pagamento = CriarPagamentoConfirmado();
        var indicacao = CriarIndicacaoVinculada(pagamento.VistoriaId, Guid.NewGuid());
        var existente = Cashback.Criar(indicacao.Id, pagamento.Id, indicacao.UsuarioIndicadorId, pagamento.Valor);
        pagamentoRepository
            .Setup(repository => repository.ObterPorIdAsync(pagamento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);
        indicacaoRepository
            .Setup(repository => repository.ObterPorVistoriaIdAsync(pagamento.VistoriaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indicacao);
        cashbackRepository
            .Setup(repository => repository.ObterPorPagamentoVistoriaIdAsync(pagamento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existente);

        await Assert.ThrowsAsync<CashbackJaExisteException>(() =>
            CriarService(cashbackRepository, indicacaoRepository, pagamentoRepository)
                .GerarPorPagamentoAsync(pagamento.Id));

        cashbackRepository.Verify(
            repository => repository.AdicionarAsync(It.IsAny<Cashback>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AprovarAsync_QuandoPendente_DeveAtualizarUmaVezERepeticaoDeveSerIdempotente()
    {
        var cashbackRepository = new Mock<ICashbackRepository>();
        var cashback = CriarCashback();
        var cancellationToken = new CancellationTokenSource().Token;
        cashbackRepository
            .Setup(repository => repository.ObterPorIdAsync(cashback.Id, cancellationToken))
            .ReturnsAsync(cashback);
        cashbackRepository
            .Setup(repository => repository.AtualizarAsync(cashback, cancellationToken))
            .Returns(Task.CompletedTask);
        var service = CriarService(cashbackRepository, new Mock<IIndicacaoRepository>(), new Mock<IPagamentoVistoriaRepository>());

        await service.AprovarAsync(cashback.Id, cancellationToken);
        await service.AprovarAsync(cashback.Id, cancellationToken);

        Assert.Equal(StatusCashback.Disponivel, cashback.Status);
        cashbackRepository.Verify(repository => repository.AtualizarAsync(cashback, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task AprovarAsync_QuandoCashbackNaoExistir_DeveLancarExcecaoEspecifica()
    {
        var cashbackRepository = new Mock<ICashbackRepository>();
        cashbackRepository
            .Setup(repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cashback?)null);

        await Assert.ThrowsAsync<CashbackNaoEncontradoException>(() =>
            CriarService(cashbackRepository, new Mock<IIndicacaoRepository>(), new Mock<IPagamentoVistoriaRepository>())
                .AprovarAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CancelarAsync_QuandoPendenteOuDisponivel_DeveAtualizarSemAlterarSnapshot()
    {
        var cashbackRepository = new Mock<ICashbackRepository>();
        var cashback = CriarCashback();
        cashback.Aprovar();
        var valorTotalPago = cashback.ValorTotalPago;
        var percentual = cashback.Percentual;
        var valor = cashback.Valor;
        cashbackRepository
            .Setup(repository => repository.ObterPorIdAsync(cashback.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cashback);
        cashbackRepository
            .Setup(repository => repository.AtualizarAsync(cashback, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CriarService(cashbackRepository, new Mock<IIndicacaoRepository>(), new Mock<IPagamentoVistoriaRepository>())
            .CancelarAsync(cashback.Id);

        Assert.Equal(StatusCashback.Cancelado, cashback.Status);
        Assert.Equal(valorTotalPago, cashback.ValorTotalPago);
        Assert.Equal(percentual, cashback.Percentual);
        Assert.Equal(valor, cashback.Valor);
        cashbackRepository.Verify(
            repository => repository.AtualizarAsync(cashback, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void GerarPorPagamentoAsync_DeveAceitarSomenteIdentificadorDoPagamentoComoInputDeNegocio()
    {
        var metodo = typeof(ICashbackService).GetMethod(nameof(ICashbackService.GerarPorPagamentoAsync));

        Assert.NotNull(metodo);
        var parametros = metodo.GetParameters();
        Assert.Equal(2, parametros.Length);
        Assert.Equal(typeof(Guid), parametros[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parametros[1].ParameterType);
    }

    private static CashbackService CriarService(
        Mock<ICashbackRepository> cashbackRepository,
        Mock<IIndicacaoRepository> indicacaoRepository,
        Mock<IPagamentoVistoriaRepository> pagamentoRepository) =>
        new(cashbackRepository.Object, indicacaoRepository.Object, pagamentoRepository.Object);

    private static PagamentoVistoria CriarPagamentoConfirmado(decimal valor = 500m)
    {
        var pagamento = new PagamentoVistoria(Guid.NewGuid(), valor);
        pagamento.Confirmar();
        return pagamento;
    }

    private static Indicacao CriarIndicacaoVinculada(Guid vistoriaId, Guid indicadorId)
    {
        var indicacao = new Indicacao(indicadorId, "Ana Indicada", "11999999999", "A2-123");
        indicacao.VincularVistoria(vistoriaId);
        return indicacao;
    }

    private static Cashback CriarCashback() => Cashback.Criar(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        500m);
}

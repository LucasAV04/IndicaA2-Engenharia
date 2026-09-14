using Application.Interfaces.Services;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class PagamentoPixServiceTests
{
    [Fact]
    public async Task CriarPorCashbackAsync_QuandoCashbackDisponivelEDadosPixExistirem_DeveCriarSnapshotSemAlterarCashback()
    {
        var cashback = CriarCashbackDisponivel(499.90m);
        var dadosPix = new DadosPix(cashback.UsuarioIndicadorId, TipoChavePix.Email, "indicador@exemplo.com");
        var cashbackRepository = new Mock<ICashbackRepository>();
        var dadosPixRepository = new Mock<IDadosPixRepository>();
        var pagamentoPixRepository = new Mock<IPagamentoPixRepository>();
        var cancellationToken = new CancellationTokenSource().Token;
        PagamentoPix? persistido = null;
        cashbackRepository
            .Setup(repository => repository.ObterPorIdAsync(cashback.Id, cancellationToken))
            .ReturnsAsync(cashback);
        pagamentoPixRepository
            .Setup(repository => repository.ObterPorCashbackIdAsync(cashback.Id, cancellationToken))
            .ReturnsAsync((PagamentoPix?)null);
        dadosPixRepository
            .Setup(repository => repository.ObterPorUsuarioIdAsync(cashback.UsuarioIndicadorId, cancellationToken))
            .ReturnsAsync(dadosPix);
        pagamentoPixRepository
            .Setup(repository => repository.AdicionarAsync(It.IsAny<PagamentoPix>(), cancellationToken))
            .Callback<PagamentoPix, CancellationToken>((pagamentoPix, _) => persistido = pagamentoPix)
            .Returns(Task.CompletedTask);

        var resultado = await CriarService(cashbackRepository, dadosPixRepository, pagamentoPixRepository)
            .CriarPorCashbackAsync(cashback.Id, cancellationToken);

        Assert.NotNull(persistido);
        Assert.Equal(cashback.Id, resultado.CashbackId);
        Assert.Equal(cashback.UsuarioIndicadorId, resultado.UsuarioBeneficiarioId);
        Assert.Equal(cashback.Valor, resultado.Valor);
        Assert.Equal(dadosPix.TipoChavePix, resultado.TipoChavePix);
        Assert.Equal(StatusPagamentoPix.Pendente, resultado.Status);
        Assert.Equal(0, resultado.QuantidadeTentativas);
        Assert.Equal(StatusCashback.Disponivel, cashback.Status);
        Assert.Equal(dadosPix.ChavePix, persistido!.ChavePix);
        cashbackRepository.Verify(
            repository => repository.AtualizarAsync(It.IsAny<Cashback>(), It.IsAny<CancellationToken>()),
            Times.Never);
        pagamentoPixRepository.Verify(
            repository => repository.AdicionarAsync(
                It.Is<PagamentoPix>(pagamentoPix =>
                    pagamentoPix.Valor == cashback.Valor &&
                    pagamentoPix.UsuarioBeneficiarioId == cashback.UsuarioIndicadorId &&
                    pagamentoPix.TipoChavePix == dadosPix.TipoChavePix &&
                    pagamentoPix.ChavePix == dadosPix.ChavePix),
                cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task CriarPorCashbackAsync_QuandoIdForVazio_NaoDeveConsultarRepositories()
    {
        var cashbackRepository = new Mock<ICashbackRepository>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CriarService(cashbackRepository, new Mock<IDadosPixRepository>(), new Mock<IPagamentoPixRepository>())
                .CriarPorCashbackAsync(Guid.Empty));

        cashbackRepository.Verify(
            repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CriarPorCashbackAsync_QuandoCashbackNaoExistir_DeveLancarExcecaoEspecifica()
    {
        var cashbackRepository = new Mock<ICashbackRepository>();
        cashbackRepository
            .Setup(repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cashback?)null);

        await Assert.ThrowsAsync<CashbackNaoEncontradoException>(() =>
            CriarService(cashbackRepository, new Mock<IDadosPixRepository>(), new Mock<IPagamentoPixRepository>())
                .CriarPorCashbackAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(StatusCashback.Pendente)]
    [InlineData(StatusCashback.Pago)]
    [InlineData(StatusCashback.Cancelado)]
    public async Task CriarPorCashbackAsync_QuandoCashbackNaoEstiverDisponivel_DeveRejeitar(
        StatusCashback status)
    {
        var cashback = CriarCashback(status);
        var pagamentoPixRepository = new Mock<IPagamentoPixRepository>();
        var cashbackRepository = new Mock<ICashbackRepository>();
        cashbackRepository
            .Setup(repository => repository.ObterPorIdAsync(cashback.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cashback);

        await Assert.ThrowsAsync<CashbackNaoElegivelParaPagamentoPixException>(() =>
            CriarService(cashbackRepository, new Mock<IDadosPixRepository>(), pagamentoPixRepository)
                .CriarPorCashbackAsync(cashback.Id));

        pagamentoPixRepository.Verify(
            repository => repository.ObterPorCashbackIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CriarPorCashbackAsync_QuandoOrdemJaExistir_DeveImpedirDuplicidade()
    {
        var cashback = CriarCashbackDisponivel();
        var pagamentoPixRepository = new Mock<IPagamentoPixRepository>();
        var cashbackRepository = new Mock<ICashbackRepository>();
        cashbackRepository
            .Setup(repository => repository.ObterPorIdAsync(cashback.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cashback);
        pagamentoPixRepository
            .Setup(repository => repository.ObterPorCashbackIdAsync(cashback.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagamentoPix.Criar(
                cashback.Id,
                cashback.UsuarioIndicadorId,
                cashback.Valor,
                TipoChavePix.Email,
                "indicador@exemplo.com"));

        await Assert.ThrowsAsync<PagamentoPixJaExisteException>(() =>
            CriarService(cashbackRepository, new Mock<IDadosPixRepository>(), pagamentoPixRepository)
                .CriarPorCashbackAsync(cashback.Id));

        pagamentoPixRepository.Verify(
            repository => repository.AdicionarAsync(It.IsAny<PagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CriarPorCashbackAsync_QuandoDadosPixNaoExistirem_DeveRejeitarSemCriarOrdem()
    {
        var cashback = CriarCashbackDisponivel();
        var cashbackRepository = new Mock<ICashbackRepository>();
        var dadosPixRepository = new Mock<IDadosPixRepository>();
        var pagamentoPixRepository = new Mock<IPagamentoPixRepository>();
        cashbackRepository
            .Setup(repository => repository.ObterPorIdAsync(cashback.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cashback);
        pagamentoPixRepository
            .Setup(repository => repository.ObterPorCashbackIdAsync(cashback.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoPix?)null);
        dadosPixRepository
            .Setup(repository => repository.ObterPorUsuarioIdAsync(cashback.UsuarioIndicadorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DadosPix?)null);

        await Assert.ThrowsAsync<DadosPixNaoCadastradosException>(() =>
            CriarService(cashbackRepository, dadosPixRepository, pagamentoPixRepository)
                .CriarPorCashbackAsync(cashback.Id));

        pagamentoPixRepository.Verify(
            repository => repository.AdicionarAsync(It.IsAny<PagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ObterPorIdAsync_QuandoOrdemExistir_DeveRetornarRespostaSemExporChavePix()
    {
        var pagamentoPix = PagamentoPix.Criar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            TipoChavePix.Email,
            "indicador@exemplo.com");
        var pagamentoPixRepository = new Mock<IPagamentoPixRepository>();
        pagamentoPixRepository
            .Setup(repository => repository.ObterPorIdAsync(pagamentoPix.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamentoPix);

        var resultado = await CriarService(
                new Mock<ICashbackRepository>(),
                new Mock<IDadosPixRepository>(),
                pagamentoPixRepository)
            .ObterPorIdAsync(pagamentoPix.Id);

        Assert.Equal(pagamentoPix.Id, resultado.Id);
        Assert.Null(typeof(Application.DTOs.PagamentoPix.PagamentoPixResponseDto)
            .GetProperty(nameof(PagamentoPix.ChavePix)));
    }

    [Fact]
    public async Task ObterPorCashbackEPorBeneficiario_DevemPropagarCancellationToken()
    {
        var cashbackId = Guid.NewGuid();
        var beneficiarioId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var pagamentoPix = PagamentoPix.Criar(
            cashbackId,
            beneficiarioId,
            100m,
            TipoChavePix.Email,
            "indicador@exemplo.com");
        var pagamentoPixRepository = new Mock<IPagamentoPixRepository>();
        pagamentoPixRepository
            .Setup(repository => repository.ObterPorCashbackIdAsync(cashbackId, cancellationToken))
            .ReturnsAsync(pagamentoPix);
        pagamentoPixRepository
            .Setup(repository => repository.ObterPorUsuarioBeneficiarioIdAsync(beneficiarioId, cancellationToken))
            .ReturnsAsync([pagamentoPix]);
        var service = CriarService(
            new Mock<ICashbackRepository>(),
            new Mock<IDadosPixRepository>(),
            pagamentoPixRepository);

        await service.ObterPorCashbackIdAsync(cashbackId, cancellationToken);
        await service.ObterPorUsuarioBeneficiarioIdAsync(beneficiarioId, cancellationToken);

        pagamentoPixRepository.Verify(
            repository => repository.ObterPorCashbackIdAsync(cashbackId, cancellationToken),
            Times.Once);
        pagamentoPixRepository.Verify(
            repository => repository.ObterPorUsuarioBeneficiarioIdAsync(beneficiarioId, cancellationToken),
            Times.Once);
    }

    [Fact]
    public void CriarPorCashbackAsync_DeveAceitarSomenteIdentificadorDoCashbackComoInputDeNegocio()
    {
        var metodo = typeof(IPagamentoPixService).GetMethod(nameof(IPagamentoPixService.CriarPorCashbackAsync));

        Assert.NotNull(metodo);
        var parametros = metodo.GetParameters();
        Assert.Equal(2, parametros.Length);
        Assert.Equal(typeof(Guid), parametros[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parametros[1].ParameterType);
    }

    [Fact]
    public async Task CancelarAsync_DeveAplicarTransicaoDoDominioEPersistirComCancellationToken()
    {
        var pagamentoPix = PagamentoPix.Criar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            TipoChavePix.Email,
            "indicador@exemplo.com");
        var cancellationToken = new CancellationTokenSource().Token;
        var pagamentoPixRepository = new Mock<IPagamentoPixRepository>();
        pagamentoPixRepository
            .Setup(repository => repository.ObterPorIdAsync(pagamentoPix.Id, cancellationToken))
            .ReturnsAsync(pagamentoPix);
        pagamentoPixRepository
            .Setup(repository => repository.AtualizarAsync(pagamentoPix, cancellationToken))
            .Returns(Task.CompletedTask);
        var service = CriarService(
            new Mock<ICashbackRepository>(),
            new Mock<IDadosPixRepository>(),
            pagamentoPixRepository);

        await service.CancelarAsync(pagamentoPix.Id, cancellationToken);

        Assert.Equal(StatusPagamentoPix.Cancelado, pagamentoPix.Status);
        pagamentoPixRepository.Verify(
            repository => repository.AtualizarAsync(pagamentoPix, cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_QuandoPagamentoNaoExistir_DeveLancarExcecaoEspecifica()
    {
        var pagamentoPixRepository = new Mock<IPagamentoPixRepository>();
        pagamentoPixRepository
            .Setup(repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoPix?)null);

        await Assert.ThrowsAsync<PagamentoPixNaoEncontradoException>(() =>
            CriarService(
                    new Mock<ICashbackRepository>(),
                    new Mock<IDadosPixRepository>(),
                    pagamentoPixRepository)
                .CancelarAsync(Guid.NewGuid()));

        pagamentoPixRepository.Verify(
            repository => repository.AtualizarAsync(It.IsAny<PagamentoPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static PagamentoPixService CriarService(
        Mock<ICashbackRepository> cashbackRepository,
        Mock<IDadosPixRepository> dadosPixRepository,
        Mock<IPagamentoPixRepository> pagamentoPixRepository) =>
        new(cashbackRepository.Object, dadosPixRepository.Object, pagamentoPixRepository.Object);

    private static Cashback CriarCashbackDisponivel(decimal valorTotalPago = 500m)
    {
        var cashback = Cashback.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), valorTotalPago);
        cashback.Aprovar();
        return cashback;
    }

    private static Cashback CriarCashback(StatusCashback status)
    {
        var instante = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        return Cashback.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            0.20m,
            100m,
            status,
            instante,
            instante);
    }
}

using Application.DTOs.PagamentoVistoria;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class PagamentoVistoriaServiceTests
{
    [Fact]
    public async Task CriarAsync_QuandoVistoriaExisteESemPagamento_DeveAdicionarERepassarCancellationToken()
    {
        var vistoria = CriarVistoria();
        var dto = CriarDto(vistoria.Id, 500m);
        var cancellationToken = new CancellationTokenSource().Token;
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(vistoria.Id, cancellationToken))
            .ReturnsAsync(vistoria);
        pagamentoRepository
            .Setup(repository => repository.ObterPorVistoriaIdAsync(vistoria.Id, cancellationToken))
            .ReturnsAsync((PagamentoVistoria?)null);

        var resultado = await CriarService(pagamentoRepository, vistoriaRepository).CriarAsync(dto, cancellationToken);

        Assert.Equal(vistoria.Id, resultado.VistoriaId);
        Assert.Equal(500m, resultado.Valor);
        Assert.Equal(StatusPagamentoVistoria.Pendente, resultado.Status);
        pagamentoRepository.Verify(
            repository => repository.AdicionarAsync(
                It.Is<PagamentoVistoria>(pagamento => pagamento.VistoriaId == vistoria.Id && pagamento.Valor == 500m),
                cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task CriarAsync_QuandoVistoriaNaoExiste_DeveLancarVistoriaNaoEncontradaException()
    {
        var dto = CriarDto(Guid.NewGuid(), 500m);
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(dto.VistoriaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vistoria?)null);

        await Assert.ThrowsAsync<VistoriaNaoEncontradaException>(() =>
            CriarService(pagamentoRepository, vistoriaRepository).CriarAsync(dto));

        pagamentoRepository.Verify(
            repository => repository.AdicionarAsync(It.IsAny<PagamentoVistoria>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CriarAsync_QuandoPagamentoJaExisteParaVistoria_DeveLancarDomainException()
    {
        var vistoria = CriarVistoria();
        var pagamentoExistente = new PagamentoVistoria(vistoria.Id, 500m);
        var vistoriaRepository = CriarVistoriaRepository(vistoria);
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        pagamentoRepository
            .Setup(repository => repository.ObterPorVistoriaIdAsync(vistoria.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamentoExistente);

        await Assert.ThrowsAsync<DomainException>(() =>
            CriarService(pagamentoRepository, vistoriaRepository).CriarAsync(CriarDto(vistoria.Id, 800m)));

        pagamentoRepository.Verify(
            repository => repository.AdicionarAsync(It.IsAny<PagamentoVistoria>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ObterPorIdAsync_QuandoPagamentoExiste_DeveRetornarResponseERepassarCancellationToken()
    {
        var pagamento = CriarPagamento();
        var cancellationToken = new CancellationTokenSource().Token;
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        pagamentoRepository
            .Setup(repository => repository.ObterPorIdAsync(pagamento.Id, cancellationToken))
            .ReturnsAsync(pagamento);

        var resultado = await CriarService(pagamentoRepository, new Mock<IVistoriaRepository>())
            .ObterPorIdAsync(pagamento.Id, cancellationToken);

        Assert.Equal(pagamento.Id, resultado.Id);
        pagamentoRepository.Verify(repository => repository.ObterPorIdAsync(pagamento.Id, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ObterPorIdAsync_QuandoPagamentoNaoExiste_DeveLancarPagamentoVistoriaNaoEncontradoException()
    {
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        pagamentoRepository
            .Setup(repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagamentoVistoria?)null);

        await Assert.ThrowsAsync<PagamentoVistoriaNaoEncontradoException>(() =>
            CriarService(pagamentoRepository, new Mock<IVistoriaRepository>()).ObterPorIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ObterPorVistoriaIdAsync_QuandoPagamentoExiste_DeveRetornarResponse()
    {
        var pagamento = CriarPagamento();
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        pagamentoRepository
            .Setup(repository => repository.ObterPorVistoriaIdAsync(pagamento.VistoriaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);

        var resultado = await CriarService(pagamentoRepository, new Mock<IVistoriaRepository>())
            .ObterPorVistoriaIdAsync(pagamento.VistoriaId);

        Assert.Equal(pagamento.Id, resultado.Id);
    }

    [Fact]
    public async Task ObterPorVistoriaIdAsync_QuandoPagamentoNaoExiste_DeveLancarPagamentoVistoriaNaoEncontradoExceptionERepassarCancellationToken()
    {
        var vistoriaId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        pagamentoRepository
            .Setup(repository => repository.ObterPorVistoriaIdAsync(vistoriaId, cancellationToken))
            .ReturnsAsync((PagamentoVistoria?)null);

        await Assert.ThrowsAsync<PagamentoVistoriaNaoEncontradoException>(() =>
            CriarService(pagamentoRepository, new Mock<IVistoriaRepository>())
                .ObterPorVistoriaIdAsync(vistoriaId, cancellationToken));

        pagamentoRepository.Verify(
            repository => repository.ObterPorVistoriaIdAsync(vistoriaId, cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task ObterTodosAsync_DeveRetornarColecaoDoRepository()
    {
        IReadOnlyCollection<PagamentoVistoria> pagamentos = [CriarPagamento()];
        var pagamentoRepository = new Mock<IPagamentoVistoriaRepository>();
        pagamentoRepository
            .Setup(repository => repository.ObterTodosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamentos);

        var resultado = await CriarService(pagamentoRepository, new Mock<IVistoriaRepository>()).ObterTodosAsync();

        Assert.Single(resultado);
    }

    [Fact]
    public async Task ConfirmarAsync_QuandoPendente_DeveAtualizarPagamento()
    {
        var pagamento = CriarPagamento();
        var pagamentoRepository = CriarPagamentoRepository(pagamento);

        await CriarService(pagamentoRepository, new Mock<IVistoriaRepository>()).ConfirmarAsync(pagamento.Id);

        Assert.Equal(StatusPagamentoVistoria.Confirmado, pagamento.Status);
        Assert.NotNull(pagamento.PagoEm);
        pagamentoRepository.Verify(
            repository => repository.AtualizarAsync(pagamento, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConfirmarAsync_QuandoJaConfirmado_NaoDevePersistirNovamente()
    {
        var pagamento = CriarPagamento();
        pagamento.Confirmar();
        var pagamentoRepository = CriarPagamentoRepository(pagamento);

        await CriarService(pagamentoRepository, new Mock<IVistoriaRepository>()).ConfirmarAsync(pagamento.Id);

        pagamentoRepository.Verify(
            repository => repository.AtualizarAsync(It.IsAny<PagamentoVistoria>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelarAsync_QuandoPendente_DeveAtualizarPagamento()
    {
        var pagamento = CriarPagamento();
        var pagamentoRepository = CriarPagamentoRepository(pagamento);

        await CriarService(pagamentoRepository, new Mock<IVistoriaRepository>()).CancelarAsync(pagamento.Id);

        Assert.Equal(StatusPagamentoVistoria.Cancelado, pagamento.Status);
        Assert.Null(pagamento.PagoEm);
        pagamentoRepository.Verify(
            repository => repository.AtualizarAsync(pagamento, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_QuandoJaCancelado_NaoDevePersistirNovamente()
    {
        var pagamento = CriarPagamento();
        pagamento.Cancelar();
        var pagamentoRepository = CriarPagamentoRepository(pagamento);

        await CriarService(pagamentoRepository, new Mock<IVistoriaRepository>()).CancelarAsync(pagamento.Id);

        pagamentoRepository.Verify(
            repository => repository.AtualizarAsync(It.IsAny<PagamentoVistoria>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static PagamentoVistoriaService CriarService(
        Mock<IPagamentoVistoriaRepository> pagamentoRepository,
        Mock<IVistoriaRepository> vistoriaRepository) =>
        new(pagamentoRepository.Object, vistoriaRepository.Object);

    private static Mock<IVistoriaRepository> CriarVistoriaRepository(Vistoria vistoria)
    {
        var repository = new Mock<IVistoriaRepository>();
        repository
            .Setup(item => item.ObterPorIdAsync(vistoria.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vistoria);
        return repository;
    }

    private static Mock<IPagamentoVistoriaRepository> CriarPagamentoRepository(PagamentoVistoria pagamento)
    {
        var repository = new Mock<IPagamentoVistoriaRepository>();
        repository
            .Setup(item => item.ObterPorIdAsync(pagamento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);
        return repository;
    }

    private static CreatePagamentoVistoriaDto CriarDto(Guid vistoriaId, decimal valor) => new()
    {
        VistoriaId = vistoriaId,
        Valor = valor
    };

    private static PagamentoVistoria CriarPagamento() => new(Guid.NewGuid(), 500m);

    private static Vistoria CriarVistoria() => new(
        Guid.NewGuid(),
        "Apartamento",
        70m,
        PacoteVistoria.Simples,
        DateTime.UtcNow);
}

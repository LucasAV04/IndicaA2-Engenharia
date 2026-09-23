using Application.DTOs.Vistoria;
using Application.Services;
using Application.Interfaces.Stores;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Exceptions.Usuario;
using Domain.Interfaces;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class VistoriaServiceTests
{
    [Fact]
    public async Task CriarAsync_QuandoUsuarioExiste_DeveAdicionarVistoriaERepassarCancellationToken()
    {
        var usuarioId = Guid.NewGuid();
        var dto = CriarDto(usuarioId);
        var cancellationToken = new CancellationTokenSource().Token;
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var usuarioRepository = new Mock<IUsuarioRepository>();
        usuarioRepository.Setup(repository => repository.ObterPorIdAsync(usuarioId, cancellationToken))
            .ReturnsAsync(new Usuario("Teste", "teste@example.invalid", "hash-ficticio", codigoIndicacao: "TEST1234"));
        var store = new Mock<IPrecificacaoStore>();
        store.Setup(s => s.CriarVistoriaAsync(usuarioId, dto.TipoPlantaId, dto.AreaM2, dto.Pacote, dto.DataAgendada, It.IsAny<DateTime>(), cancellationToken))
            .ReturnsAsync(CriarVistoria(usuarioId));

        var resultado = await new VistoriaService(vistoriaRepository.Object, usuarioRepository.Object, store.Object, TimeProvider.System).CriarAsync(dto, cancellationToken);

        Assert.Equal(usuarioId, resultado.UsuarioId);
        Assert.Equal(StatusVistoria.Agendada, resultado.Status);
        store.Verify(s => s.CriarVistoriaAsync(usuarioId, dto.TipoPlantaId, dto.AreaM2, dto.Pacote, dto.DataAgendada, It.IsAny<DateTime>(), cancellationToken), Times.Once);
        vistoriaRepository.Verify(r => r.AdicionarAsync(It.IsAny<Vistoria>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CriarAsync_QuandoUsuarioNaoExiste_DeveLancarUsuarioNaoEncontradoException()
    {
        var dto = CriarDto(Guid.NewGuid());
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        var usuarioRepository = new Mock<IUsuarioRepository>();
        usuarioRepository.Setup(repository => repository.ExistePorIdAsync(dto.UsuarioId)).ReturnsAsync(false);

        await Assert.ThrowsAsync<UsuarioNaoEncontradoException>(() =>
            CriarService(vistoriaRepository, usuarioRepository).CriarAsync(dto));

        vistoriaRepository.Verify(
            repository => repository.AdicionarAsync(It.IsAny<Vistoria>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ObterPorIdAsync_QuandoVistoriaExiste_DeveRetornarResponseERepassarCancellationToken()
    {
        var vistoria = CriarVistoria();
        var cancellationToken = new CancellationTokenSource().Token;
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(vistoria.Id, cancellationToken))
            .ReturnsAsync(vistoria);

        var resultado = await CriarService(vistoriaRepository, new Mock<IUsuarioRepository>())
            .ObterPorIdAsync(vistoria.Id, cancellationToken);

        Assert.Equal(vistoria.Id, resultado.Id);
        vistoriaRepository.Verify(repository => repository.ObterPorIdAsync(vistoria.Id, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ObterPorIdAsync_QuandoVistoriaNaoExiste_DeveLancarVistoriaNaoEncontradaException()
    {
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        vistoriaRepository
            .Setup(repository => repository.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vistoria?)null);

        await Assert.ThrowsAsync<VistoriaNaoEncontradaException>(() =>
            CriarService(vistoriaRepository, new Mock<IUsuarioRepository>()).ObterPorIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ObterTodasAsync_DeveRetornarColecaoDoRepository()
    {
        IReadOnlyCollection<Vistoria> vistorias = [CriarVistoria()];
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        vistoriaRepository
            .Setup(repository => repository.ObterTodasAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(vistorias);

        var resultado = await CriarService(vistoriaRepository, new Mock<IUsuarioRepository>()).ObterTodasAsync();

        Assert.Single(resultado);
    }

    [Fact]
    public async Task ObterPorUsuarioIdAsync_DeveRetornarColecaoDoRepository()
    {
        var usuarioId = Guid.NewGuid();
        IReadOnlyCollection<Vistoria> vistorias = [CriarVistoria(usuarioId)];
        var vistoriaRepository = new Mock<IVistoriaRepository>();
        vistoriaRepository
            .Setup(repository => repository.ObterPorUsuarioIdAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vistorias);

        var resultado = await CriarService(vistoriaRepository, new Mock<IUsuarioRepository>())
            .ObterPorUsuarioIdAsync(usuarioId);

        Assert.Single(resultado);
        Assert.Equal(usuarioId, resultado.Single().UsuarioId);
    }

    [Fact]
    public async Task MarcarRealizadaAsync_DeveAlterarVistoriaEPersistir()
    {
        var vistoria = CriarVistoria();
        var vistoriaRepository = CriarRepositoryComVistoria(vistoria);

        await CriarService(vistoriaRepository, new Mock<IUsuarioRepository>()).MarcarRealizadaAsync(vistoria.Id);

        Assert.Equal(StatusVistoria.Realizada, vistoria.Status);
        vistoriaRepository.Verify(
            repository => repository.AtualizarAsync(vistoria, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConcluirAsync_DeveAlterarVistoriaEPersistir()
    {
        var vistoria = CriarVistoria();
        vistoria.MarcarRealizada();
        var vistoriaRepository = CriarRepositoryComVistoria(vistoria);

        await CriarService(vistoriaRepository, new Mock<IUsuarioRepository>()).ConcluirAsync(vistoria.Id);

        Assert.Equal(StatusVistoria.Concluida, vistoria.Status);
        vistoriaRepository.Verify(
            repository => repository.AtualizarAsync(vistoria, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_DeveAlterarVistoriaEPersistir()
    {
        var vistoria = CriarVistoria();
        var vistoriaRepository = CriarRepositoryComVistoria(vistoria);

        await CriarService(vistoriaRepository, new Mock<IUsuarioRepository>()).CancelarAsync(vistoria.Id);

        Assert.Equal(StatusVistoria.Cancelada, vistoria.Status);
        vistoriaRepository.Verify(
            repository => repository.AtualizarAsync(vistoria, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_QuandoJaCancelada_NaoDevePersistirNovamente()
    {
        var vistoria = CriarVistoria();
        vistoria.Cancelar();
        var vistoriaRepository = CriarRepositoryComVistoria(vistoria);

        await CriarService(vistoriaRepository, new Mock<IUsuarioRepository>()).CancelarAsync(vistoria.Id);

        vistoriaRepository.Verify(
            repository => repository.AtualizarAsync(It.IsAny<Vistoria>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static VistoriaService CriarService(
        Mock<IVistoriaRepository> vistoriaRepository,
        Mock<IUsuarioRepository> usuarioRepository) =>
        new(vistoriaRepository.Object, usuarioRepository.Object, Mock.Of<IPrecificacaoStore>(), TimeProvider.System);

    private static Mock<IVistoriaRepository> CriarRepositoryComVistoria(Vistoria vistoria)
    {
        var repository = new Mock<IVistoriaRepository>();
        repository
            .Setup(item => item.ObterPorIdAsync(vistoria.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vistoria);
        return repository;
    }

    private static CreateVistoriaDto CriarDto(Guid usuarioId) => new()
    {
        UsuarioId = usuarioId,
        TipoPlantaId = Guid.NewGuid(),
        AreaM2 = 70m,
        Pacote = PacoteVistoria.Simples,
        DataAgendada = DateTime.UtcNow
    };

    private static Vistoria CriarVistoria(Guid? usuarioId = null) => new(
        usuarioId ?? Guid.NewGuid(),
        "Apartamento",
        70m,
        PacoteVistoria.Simples,
        DateTime.UtcNow);
}

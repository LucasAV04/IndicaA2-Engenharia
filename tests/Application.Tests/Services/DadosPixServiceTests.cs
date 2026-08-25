using Application.DTOs.DadosPix;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions.Usuario;
using Domain.Interfaces;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class DadosPixServiceTests
{
    [Fact]
    public async Task CadastrarOuAtualizarAsync_QuandoUsuarioExisteDeveCadastrarENormalizarChave()
    {
        var usuarioId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var dadosPixRepository = new Mock<IDadosPixRepository>();
        var usuarioRepository = CriarUsuarioRepositoryExistente(usuarioId);
        dadosPixRepository
            .Setup(repository => repository.ObterPorUsuarioIdAsync(usuarioId, cancellationToken))
            .ReturnsAsync((DadosPix?)null);

        var resultado = await CriarService(dadosPixRepository, usuarioRepository)
            .CadastrarOuAtualizarAsync(usuarioId, CriarDto(TipoChavePix.Cpf, "123.456.789-09"), cancellationToken);

        Assert.Equal("12345678909", resultado.ChavePix);
        dadosPixRepository.Verify(
            repository => repository.AdicionarAsync(
                It.Is<DadosPix>(dadosPix => dadosPix.UsuarioId == usuarioId &&
                                           dadosPix.ChavePix == "12345678909"),
                cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task CadastrarOuAtualizarAsync_QuandoUsuarioNaoExisteDeveLancarExcecaoEspecifica()
    {
        var usuarioId = Guid.NewGuid();
        var dadosPixRepository = new Mock<IDadosPixRepository>();
        var usuarioRepository = new Mock<IUsuarioRepository>();
        usuarioRepository.Setup(repository => repository.ExistePorIdAsync(usuarioId)).ReturnsAsync(false);

        await Assert.ThrowsAsync<UsuarioNaoEncontradoException>(() =>
            CriarService(dadosPixRepository, usuarioRepository)
                .CadastrarOuAtualizarAsync(usuarioId, CriarDto(TipoChavePix.Email, "pix@exemplo.com")));

        dadosPixRepository.Verify(
            repository => repository.AdicionarAsync(It.IsAny<DadosPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CadastrarOuAtualizarAsync_QuandoDadosExistemDeveAtualizarInclusiveTipo()
    {
        var usuarioId = Guid.NewGuid();
        var dadosPix = new DadosPix(usuarioId, TipoChavePix.Cpf, "12345678909");
        var dadosPixRepository = new Mock<IDadosPixRepository>();
        dadosPixRepository
            .Setup(repository => repository.ObterPorUsuarioIdAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dadosPix);

        var resultado = await CriarService(dadosPixRepository, CriarUsuarioRepositoryExistente(usuarioId))
            .CadastrarOuAtualizarAsync(usuarioId, CriarDto(TipoChavePix.Email, "  Novo@Exemplo.Com "));

        Assert.Equal(TipoChavePix.Email, resultado.TipoChavePix);
        Assert.Equal("novo@exemplo.com", resultado.ChavePix);
        dadosPixRepository.Verify(
            repository => repository.AtualizarAsync(dadosPix, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ObterPorUsuarioIdAsync_QuandoExisteDeveRetornarResposta()
    {
        var usuarioId = Guid.NewGuid();
        var dadosPix = new DadosPix(usuarioId, TipoChavePix.Email, "pix@exemplo.com");
        var cancellationToken = new CancellationTokenSource().Token;
        var dadosPixRepository = new Mock<IDadosPixRepository>();
        dadosPixRepository
            .Setup(repository => repository.ObterPorUsuarioIdAsync(usuarioId, cancellationToken))
            .ReturnsAsync(dadosPix);

        var resultado = await CriarService(dadosPixRepository, CriarUsuarioRepositoryExistente(usuarioId))
            .ObterPorUsuarioIdAsync(usuarioId, cancellationToken);

        Assert.NotNull(resultado);
        Assert.Equal(dadosPix.Id, resultado.Id);
        dadosPixRepository.Verify(
            repository => repository.ObterPorUsuarioIdAsync(usuarioId, cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task ObterPorUsuarioIdAsync_QuandoAusenteDeveRetornarNulo()
    {
        var usuarioId = Guid.NewGuid();
        var dadosPixRepository = new Mock<IDadosPixRepository>();
        dadosPixRepository
            .Setup(repository => repository.ObterPorUsuarioIdAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DadosPix?)null);

        var resultado = await CriarService(dadosPixRepository, CriarUsuarioRepositoryExistente(usuarioId))
            .ObterPorUsuarioIdAsync(usuarioId);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task RemoverAsync_QuandoDadosExistemDeveRemoverERepassarCancellationToken()
    {
        var usuarioId = Guid.NewGuid();
        var dadosPix = new DadosPix(usuarioId, TipoChavePix.Email, "pix@exemplo.com");
        var cancellationToken = new CancellationTokenSource().Token;
        var dadosPixRepository = new Mock<IDadosPixRepository>();
        dadosPixRepository
            .Setup(repository => repository.ObterPorUsuarioIdAsync(usuarioId, cancellationToken))
            .ReturnsAsync(dadosPix);

        await CriarService(dadosPixRepository, CriarUsuarioRepositoryExistente(usuarioId))
            .RemoverAsync(usuarioId, cancellationToken);

        dadosPixRepository.Verify(repository => repository.RemoverAsync(dadosPix, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task RemoverAsync_QuandoDadosEstaoAusentesDeveSerIdempotente()
    {
        var usuarioId = Guid.NewGuid();
        var dadosPixRepository = new Mock<IDadosPixRepository>();
        dadosPixRepository
            .Setup(repository => repository.ObterPorUsuarioIdAsync(usuarioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DadosPix?)null);

        await CriarService(dadosPixRepository, CriarUsuarioRepositoryExistente(usuarioId)).RemoverAsync(usuarioId);

        dadosPixRepository.Verify(
            repository => repository.RemoverAsync(It.IsAny<DadosPix>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Service_DeveDependerSomenteDosRepositoriesNecessariosAoModulo()
    {
        var construtor = typeof(DadosPixService).GetConstructors().Single();
        var tipos = construtor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Equal([typeof(IDadosPixRepository), typeof(IUsuarioRepository)], tipos);
    }

    private static DadosPixService CriarService(
        Mock<IDadosPixRepository> dadosPixRepository,
        Mock<IUsuarioRepository> usuarioRepository) =>
        new(dadosPixRepository.Object, usuarioRepository.Object);

    private static Mock<IUsuarioRepository> CriarUsuarioRepositoryExistente(Guid usuarioId)
    {
        var usuarioRepository = new Mock<IUsuarioRepository>();
        usuarioRepository.Setup(repository => repository.ExistePorIdAsync(usuarioId)).ReturnsAsync(true);
        return usuarioRepository;
    }

    private static DadosPixDto CriarDto(TipoChavePix tipoChavePix, string chavePix) => new()
    {
        TipoChavePix = tipoChavePix,
        ChavePix = chavePix
    };
}

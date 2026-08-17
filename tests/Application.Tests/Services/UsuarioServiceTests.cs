using Application.DTOs.Usuario;
using Application.Interfaces.Security;
using Domain.Entities;
using Domain.Exceptions.Usuario;
using Domain.Interfaces;
using IndicA2.Application.Services;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class UsuarioServiceTests
{
    [Fact]
    public async Task CriarAsync_DeveGerarCodigoNormalizadoEPersistirUsuario()
    {
        var repository = new Mock<IUsuarioRepository>();
        var hasher = new Mock<IPasswordHasher>();
        var generator = new Mock<ICodigoIndicacaoGenerator>();
        var cancellationToken = new CancellationTokenSource().Token;
        Usuario? persistido = null;
        repository.Setup(item => item.ExistePorEmailAsync("ana@exemplo.com", null, cancellationToken)).ReturnsAsync(false);
        repository.Setup(item => item.ObterPorCodigoIndicacaoAsync("7K4M9P2Q", cancellationToken)).ReturnsAsync((Usuario?)null);
        repository.Setup(item => item.AdicionarAsync(It.IsAny<Usuario>(), cancellationToken))
            .Callback<Usuario, CancellationToken>((usuario, _) => persistido = usuario)
            .Returns(Task.CompletedTask);
        hasher.Setup(item => item.HashPassword("Senha123!")).Returns("hash-seguro");
        generator.Setup(item => item.Gerar()).Returns("7k4m9p2q");
        var service = CriarService(repository, hasher, generator);

        var response = await service.CriarAsync(CriarDto(), cancellationToken);

        Assert.NotNull(persistido);
        Assert.Equal("7K4M9P2Q", persistido.CodigoIndicacao);
        Assert.Equal("7K4M9P2Q", response.CodigoIndicacao);
        repository.Verify(item => item.AdicionarAsync(persistido!, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_QuandoCodigoColidir_DeveTentarNovamente()
    {
        var repository = new Mock<IUsuarioRepository>();
        var generator = new Mock<ICodigoIndicacaoGenerator>();
        var usuarioExistente = new Usuario("Existente", "existente@exemplo.com", "hash", codigoIndicacao: "AAAAAAAA");
        repository.Setup(item => item.ExistePorEmailAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.SetupSequence(item => item.ObterPorCodigoIndicacaoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuarioExistente)
            .ReturnsAsync((Usuario?)null);
        generator.SetupSequence(item => item.Gerar())
            .Returns("AAAAAAAA")
            .Returns("BBBBBBBB");
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(item => item.HashPassword(It.IsAny<string>())).Returns("hash-seguro");
        var service = CriarService(repository, hasher, generator);

        var response = await service.CriarAsync(CriarDto());

        Assert.Equal("BBBBBBBB", response.CodigoIndicacao);
        generator.Verify(item => item.Gerar(), Times.Exactly(2));
    }

    [Fact]
    public async Task CriarAsync_QuandoAtingirMaximoDeColisoes_DeveLancarExcecaoClara()
    {
        var repository = new Mock<IUsuarioRepository>();
        var generator = new Mock<ICodigoIndicacaoGenerator>();
        repository.Setup(item => item.ExistePorEmailAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.Setup(item => item.ObterPorCodigoIndicacaoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Usuario("Existente", "existente@exemplo.com", "hash", codigoIndicacao: "AAAAAAAA"));
        generator.Setup(item => item.Gerar()).Returns("AAAAAAAA");
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(item => item.HashPassword(It.IsAny<string>())).Returns("hash-seguro");
        var service = CriarService(repository, hasher, generator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CriarAsync(CriarDto()));

        Assert.Contains("cinco tentativas", exception.Message);
        generator.Verify(item => item.Gerar(), Times.Exactly(5));
    }

    [Fact]
    public async Task CriarAsync_QuandoInsertColidirPorCodigo_DeveGerarNovoCodigoEPersistirNaSegundaTentativa()
    {
        var repository = new Mock<IUsuarioRepository>();
        var hasher = new Mock<IPasswordHasher>();
        var generator = new Mock<ICodigoIndicacaoGenerator>();
        var usuariosPersistidos = new List<Usuario>();
        repository.Setup(item => item.ExistePorEmailAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.Setup(item => item.ObterPorCodigoIndicacaoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Usuario?)null);
        repository.Setup(item => item.AdicionarAsync(It.IsAny<Usuario>(), It.IsAny<CancellationToken>()))
            .Returns((Usuario usuario, CancellationToken _) =>
            {
                usuariosPersistidos.Add(usuario);
                return usuariosPersistidos.Count == 1
                    ? Task.FromException(new CodigoIndicacaoDuplicadoException())
                    : Task.CompletedTask;
            });
        hasher.Setup(item => item.HashPassword("Senha123!")).Returns("hash-seguro");
        generator.SetupSequence(item => item.Gerar())
            .Returns("AAAAAAAA")
            .Returns("BBBBBBBB");
        var service = CriarService(repository, hasher, generator);

        var response = await service.CriarAsync(CriarDto());

        Assert.Equal("BBBBBBBB", response.CodigoIndicacao);
        Assert.Equal("AAAAAAAA", usuariosPersistidos[0].CodigoIndicacao);
        Assert.Equal("BBBBBBBB", usuariosPersistidos[1].CodigoIndicacao);
        hasher.Verify(item => item.HashPassword("Senha123!"), Times.Once);
        generator.Verify(item => item.Gerar(), Times.Exactly(2));
    }

    [Fact]
    public async Task CriarAsync_QuandoCincoInsertsColidiremPorCodigo_DeveFalharAposMaximoDeTentativas()
    {
        var repository = new Mock<IUsuarioRepository>();
        var hasher = new Mock<IPasswordHasher>();
        var generator = new Mock<ICodigoIndicacaoGenerator>();
        repository.Setup(item => item.ExistePorEmailAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository.Setup(item => item.ObterPorCodigoIndicacaoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Usuario?)null);
        repository.Setup(item => item.AdicionarAsync(It.IsAny<Usuario>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CodigoIndicacaoDuplicadoException());
        hasher.Setup(item => item.HashPassword("Senha123!")).Returns("hash-seguro");
        generator.Setup(item => item.Gerar()).Returns("AAAAAAAA");
        var service = CriarService(repository, hasher, generator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CriarAsync(CriarDto()));

        Assert.Contains("cinco tentativas", exception.Message);
        hasher.Verify(item => item.HashPassword("Senha123!"), Times.Once);
        generator.Verify(item => item.Gerar(), Times.Exactly(5));
        repository.Verify(item => item.AdicionarAsync(It.IsAny<Usuario>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task CriarAsync_QuandoEmailJaExistir_NaoDeveTentarGerarCodigo()
    {
        var repository = new Mock<IUsuarioRepository>();
        var hasher = new Mock<IPasswordHasher>();
        var generator = new Mock<ICodigoIndicacaoGenerator>();
        repository.Setup(item => item.ExistePorEmailAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = CriarService(repository, hasher, generator);

        await Assert.ThrowsAsync<UsuarioJaExisteException>(() => service.CriarAsync(CriarDto()));

        hasher.Verify(item => item.HashPassword(It.IsAny<string>()), Times.Never);
        generator.Verify(item => item.Gerar(), Times.Never);
        repository.Verify(item => item.AdicionarAsync(It.IsAny<Usuario>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObterPorCodigoIndicacaoAsync_DeveNormalizarCodigoERepassarCancellationToken()
    {
        var repository = new Mock<IUsuarioRepository>();
        var usuario = new Usuario("Ana", "ana@exemplo.com", "hash", codigoIndicacao: "7K4M9P2Q");
        var cancellationToken = new CancellationTokenSource().Token;
        repository.Setup(item => item.ObterPorCodigoIndicacaoAsync("7K4M9P2Q", cancellationToken)).ReturnsAsync(usuario);
        var service = CriarService(repository, new Mock<IPasswordHasher>(), new Mock<ICodigoIndicacaoGenerator>());

        var response = await service.ObterPorCodigoIndicacaoAsync(" 7k4m9p2q ", cancellationToken);

        Assert.Equal(usuario.Id, response.Id);
        repository.Verify(item => item.ObterPorCodigoIndicacaoAsync("7K4M9P2Q", cancellationToken), Times.Once);
    }

    private static UsuarioService CriarService(
        Mock<IUsuarioRepository> repository,
        Mock<IPasswordHasher> hasher,
        Mock<ICodigoIndicacaoGenerator> generator) =>
        new(repository.Object, hasher.Object, generator.Object);

    private static CreateUsuarioDto CriarDto() => new()
    {
        Nome = "Ana",
        Email = " ANA@EXEMPLO.COM ",
        Senha = "Senha123!",
        Telefone = "11999999999"
    };
}

using Application.DTOs.Auth;
using Application.Interfaces.Security;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task LoginAsync_QuandoCredenciaisValidas_DeveNormalizarEmailGerarTokenEAtualizarUsuario()
    {
        var usuario = new Usuario("Ana", "ana@exemplo.com", "hash", codigoIndicacao: "A1B2C3D4");
        var repository = new Mock<IUsuarioRepository>();
        var hasher = new Mock<IPasswordHasher>();
        var tokenGenerator = new Mock<IAccessTokenGenerator>();
        var cancellationToken = new CancellationTokenSource().Token;
        repository.Setup(item => item.ObterPorEmailAsync("ana@exemplo.com", cancellationToken)).ReturnsAsync(usuario);
        hasher.Setup(item => item.VerifyPassword("senha", "hash")).Returns(true);
        tokenGenerator.Setup(item => item.Generate(usuario)).Returns(new AccessTokenResult("token-ficticio", DateTime.UtcNow.AddHours(1)));
        var service = new AuthService(repository.Object, hasher.Object, tokenGenerator.Object);

        var response = await service.LoginAsync(new LoginRequestDto { Email = " ANA@EXEMPLO.COM ", Senha = "senha" }, cancellationToken);

        Assert.Equal(usuario.Id, response.UsuarioId);
        Assert.Equal("token-ficticio", response.AccessToken);
        Assert.NotNull(usuario.UltimoLogin);
        repository.Verify(item => item.AtualizarAsync(usuario, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_QuandoUsuarioNaoExisteOuSenhaIncorreta_DeveLancarMesmaExcecao()
    {
        var repository = new Mock<IUsuarioRepository>();
        repository.Setup(item => item.ObterPorEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Usuario?)null);
        var service = new AuthService(repository.Object, new Mock<IPasswordHasher>().Object, new Mock<IAccessTokenGenerator>().Object);
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => service.LoginAsync(new LoginRequestDto { Email = "a@b.com", Senha = "x" }));
    }

    [Theory]
    [InlineData(StatusUsuario.Inativo)]
    [InlineData(StatusUsuario.Bloqueado)]
    public async Task LoginAsync_QuandoUsuarioSemAcesso_DeveLancarExcecao(StatusUsuario status)
    {
        var usuario = new Usuario("Ana", "ana@exemplo.com", "hash", codigoIndicacao: "A1B2C3D4");
        if (status == StatusUsuario.Inativo) usuario.Inativar(); else usuario.Bloquear();
        var repository = new Mock<IUsuarioRepository>();
        var hasher = new Mock<IPasswordHasher>();
        repository.Setup(item => item.ObterPorEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(usuario);
        hasher.Setup(item => item.VerifyPassword(It.IsAny<string>(), "hash")).Returns(true);
        var service = new AuthService(repository.Object, hasher.Object, new Mock<IAccessTokenGenerator>().Object);
        await Assert.ThrowsAsync<UsuarioSemAcessoException>(() => service.LoginAsync(new LoginRequestDto { Email = "ana@exemplo.com", Senha = "senha" }));
    }
}

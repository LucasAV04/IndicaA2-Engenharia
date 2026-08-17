using Domain.Entities;
using Domain.Enums;
using Xunit;

namespace Infrastructure.Tests.Entities;

public sealed class UsuarioReidratacaoTests
{
    [Fact]
    public void Reidratar_QuandoEstadoPersistidoValido_DevePreservarTodosOsCampos()
    {
        var id = Guid.NewGuid();
        var ultimoLogin = new DateTime(2026, 8, 11, 14, 0, 0, DateTimeKind.Utc);
        var createdAt = ultimoLogin.AddDays(-1);
        var updatedAt = ultimoLogin.AddMinutes(10);

        var usuario = Usuario.Reidratar(
            id,
            "Ana Silva",
            "ana@exemplo.com",
            "hash-seguro",
            "11999999999",
            StatusUsuario.Bloqueado,
            TipoUsuario.Administrador,
            true,
            ultimoLogin,
            createdAt,
            updatedAt);

        Assert.Equal(id, usuario.Id);
        Assert.Equal("Ana Silva", usuario.Nome);
        Assert.Equal("ana@exemplo.com", usuario.Email);
        Assert.Equal("hash-seguro", usuario.SenhaHash);
        Assert.Equal("11999999999", usuario.Telefone);
        Assert.Equal(StatusUsuario.Bloqueado, usuario.Status);
        Assert.Equal(TipoUsuario.Administrador, usuario.TipoUsuario);
        Assert.True(usuario.EmailConfirmado);
        Assert.Equal(ultimoLogin, usuario.UltimoLogin);
        Assert.Equal(createdAt, usuario.CreatedAt);
        Assert.Equal(updatedAt, usuario.UpdatedAt);
    }

    [Fact]
    public void Reidratar_QuandoIdentificadorForVazio_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Usuario.Reidratar(
            Guid.Empty,
            "Ana Silva",
            "ana@exemplo.com",
            "hash-seguro",
            null,
            StatusUsuario.Ativo,
            TipoUsuario.Usuario,
            false,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "7K4M9P2Q"));
    }

    [Fact]
    public void Reidratar_QuandoStatusPersistidoForInvalido_DeveLancarArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Usuario.Reidratar(
            Guid.NewGuid(),
            "Ana Silva",
            "ana@exemplo.com",
            "hash-seguro",
            null,
            (StatusUsuario)99,
            TipoUsuario.Usuario,
            false,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Fact]
    public void Reidratar_QuandoTipoUsuarioPersistidoForInvalido_DeveLancarArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Usuario.Reidratar(
            Guid.NewGuid(),
            "Ana Silva",
            "ana@exemplo.com",
            "hash-seguro",
            null,
            StatusUsuario.Ativo,
            (TipoUsuario)99,
            false,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Fact]
    public void Reidratar_QuandoUsuarioPossuirCodigoValido_DevePreservarCodigoNormalizado()
    {
        var usuario = Usuario.Reidratar(
            Guid.NewGuid(),
            "Ana Silva",
            "ana@exemplo.com",
            "hash-seguro",
            null,
            StatusUsuario.Ativo,
            TipoUsuario.Usuario,
            false,
            null,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            " 7k4m9p2q ");

        Assert.Equal("7K4M9P2Q", usuario.CodigoIndicacao);
    }

    [Fact]
    public void Reidratar_QuandoCodigoPersistidoForInvalido_DeveLancarDomainException()
    {
        Assert.Throws<Domain.Exceptions.DomainException>(() => Usuario.Reidratar(
            Guid.NewGuid(),
            "Ana Silva",
            "ana@exemplo.com",
            "hash-seguro",
            null,
            StatusUsuario.Ativo,
            TipoUsuario.Usuario,
            false,
            null,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            "7K4M!P2Q"));
    }

    [Fact]
    public void Reidratar_QuandoAdministradorPossuirCodigoPersistido_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Usuario.Reidratar(
            Guid.NewGuid(),
            "Admin",
            "admin@exemplo.com",
            "hash-seguro",
            null,
            StatusUsuario.Ativo,
            TipoUsuario.Administrador,
            false,
            null,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            "7K4M9P2Q"));
    }
}

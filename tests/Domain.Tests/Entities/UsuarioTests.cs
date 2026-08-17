using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class UsuarioTests
{
    [Fact]
    public void NovoUsuario_QuandoCodigoIndicacaoForNull_DeveLancarExcecao()
    {
        Assert.Throws<DomainException>(() => new Usuario("Ana", "ana@exemplo.com", "hash"));
    }

    [Fact]
    public void NovoUsuario_QuandoCodigoIndicacaoForVazio_DeveLancarExcecao()
    {
        Assert.Throws<DomainException>(() => new Usuario(
            "Ana",
            "ana@exemplo.com",
            "hash",
            codigoIndicacao: " "));
    }

    [Fact]
    public void NovoUsuario_QuandoCodigoIndicacaoForValido_DeveCriar()
    {
        var usuario = new Usuario("Ana", "ana@exemplo.com", "hash", codigoIndicacao: " 7k4m9p2q ");

        Assert.Equal("7K4M9P2Q", usuario.CodigoIndicacao);
    }

    [Fact]
    public void NovoAdministrador_QuandoCodigoForNull_DeveCriar()
    {
        var administrador = new Usuario(
            "Admin",
            "admin@exemplo.com",
            "hash",
            tipoUsuario: TipoUsuario.Administrador);

        Assert.Null(administrador.CodigoIndicacao);
    }

    [Fact]
    public void NovoAdministrador_QuandoCodigoForInformado_DeveLancarExcecao()
    {
        Assert.Throws<ArgumentException>(() => new Usuario(
            "Admin",
            "admin@exemplo.com",
            "hash",
            tipoUsuario: TipoUsuario.Administrador,
            codigoIndicacao: "7K4M9P2Q"));
    }

    [Fact]
    public void ReidratarUsuarioHistorico_QuandoCodigoForNull_DevePermitir()
    {
        var usuario = Usuario.Reidratar(
            Guid.NewGuid(),
            "Ana",
            "ana@exemplo.com",
            "hash",
            null,
            StatusUsuario.Ativo,
            TipoUsuario.Usuario,
            false,
            null,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            codigoIndicacao: null);

        Assert.Null(usuario.CodigoIndicacao);
    }
}

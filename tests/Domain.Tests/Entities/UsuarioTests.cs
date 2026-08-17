using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class UsuarioTests
{
    [Fact]
    public void Construtor_QuandoCodigoValido_DeveNormalizarEPreservarCodigo()
    {
        var usuario = CriarUsuario(" 7k4m9p2q ");

        Assert.Equal("7K4M9P2Q", usuario.CodigoIndicacao);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("7K4M-2Q!")]
    public void Construtor_QuandoFormatoDoCodigoForInvalido_DeveLancarDomainException(string codigoIndicacao)
    {
        Assert.Throws<DomainException>(() => CriarUsuario(codigoIndicacao));
    }

    [Fact]
    public void Construtor_QuandoAdministradorReceberCodigo_DeveLancarArgumentExceptionEManterCodigoNulo()
    {
        var administrador = new Usuario("Admin", "admin@exemplo.com", "hash", tipoUsuario: TipoUsuario.Administrador);

        Assert.Throws<ArgumentException>(() => new Usuario(
            "Admin",
            "admin@exemplo.com",
            "hash",
            tipoUsuario: TipoUsuario.Administrador,
            codigoIndicacao: "7K4M9P2Q"));
        Assert.Null(administrador.CodigoIndicacao);
    }

    private static Usuario CriarUsuario(string? codigoIndicacao = null) =>
        new("Ana", "ana@exemplo.com", "hash", codigoIndicacao: codigoIndicacao);
}

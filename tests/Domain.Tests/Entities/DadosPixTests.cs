using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class DadosPixTests
{
    [Fact]
    public void Criar_QuandoCpfValidoDeveNormalizar()
    {
        var dadosPix = new DadosPix(Guid.NewGuid(), TipoChavePix.Cpf, "123.456.789-09");

        Assert.Equal("12345678909", dadosPix.ChavePix);
        Assert.Equal(DateTimeKind.Utc, dadosPix.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, dadosPix.UpdatedAt.Kind);
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("12345678900")]
    [InlineData("111.111.111-11")]
    public void Criar_QuandoCpfInvalidoDeveLancarDomainException(string cpf)
    {
        Assert.Throws<DomainException>(() => Criar(TipoChavePix.Cpf, cpf));
    }

    [Fact]
    public void Criar_QuandoCnpjValidoDeveNormalizar()
    {
        var dadosPix = Criar(TipoChavePix.Cnpj, "12.345.678/0001-95");

        Assert.Equal("12345678000195", dadosPix.ChavePix);
    }

    [Theory]
    [InlineData("1234567800019")]
    [InlineData("12345678000100")]
    [InlineData("11.111.111/1111-11")]
    public void Criar_QuandoCnpjInvalidoDeveLancarDomainException(string cnpj)
    {
        Assert.Throws<DomainException>(() => Criar(TipoChavePix.Cnpj, cnpj));
    }

    [Fact]
    public void Criar_QuandoEmailValidoDeveRemoverEspacosExternosENormalizar()
    {
        var dadosPix = Criar(TipoChavePix.Email, "  Chave.Pix@Exemplo.Com  ");

        Assert.Equal("chave.pix@exemplo.com", dadosPix.ChavePix);
    }

    [Theory]
    [InlineData("chavepix-exemplo.com")]
    [InlineData("chave @exemplo.com")]
    [InlineData("@exemplo.com")]
    public void Criar_QuandoEmailInvalidoDeveLancarDomainException(string email)
    {
        Assert.Throws<DomainException>(() => Criar(TipoChavePix.Email, email));
    }

    [Fact]
    public void Criar_QuandoTelefoneValidoFormatadoDeveArmazenarSomenteDigitos()
    {
        var dadosPix = Criar(TipoChavePix.Telefone, "+55 (11) 99876-5432");

        Assert.Equal("5511998765432", dadosPix.ChavePix);
    }

    [Theory]
    [InlineData("+55 (10) 99876-5432")]
    [InlineData("(11) 99876-5432")]
    [InlineData("+55 (11) 99876-54")]
    public void Criar_QuandoTelefoneInvalidoDeveLancarDomainException(string telefone)
    {
        Assert.Throws<DomainException>(() => Criar(TipoChavePix.Telefone, telefone));
    }

    [Fact]
    public void Criar_QuandoChaveAleatoriaValidaDeveNormalizar()
    {
        const string chave = "A2C96EDB-1B46-4673-A6F0-38E7EB4FFBE1";

        var dadosPix = Criar(TipoChavePix.Aleatoria, chave);

        Assert.Equal("a2c96edb-1b46-4673-a6f0-38e7eb4ffbe1", dadosPix.ChavePix);
    }

    [Fact]
    public void Criar_QuandoChaveAleatoriaInvalidaDeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() => Criar(TipoChavePix.Aleatoria, "chave-invalida"));
    }

    [Fact]
    public void Criar_QuandoUsuarioOuTipoForInvalidoDeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() => new DadosPix(Guid.Empty, TipoChavePix.Cpf, "12345678909"));
        Assert.Throws<DomainException>(() => Criar((TipoChavePix)99, "12345678909"));
    }

    [Fact]
    public void Atualizar_DevePermitirSubstituirTipoEChave()
    {
        var dadosPix = Criar(TipoChavePix.Cpf, "12345678909");
        var updatedAtAnterior = dadosPix.UpdatedAt;

        dadosPix.Atualizar(TipoChavePix.Email, "novo@exemplo.com");

        Assert.Equal(TipoChavePix.Email, dadosPix.TipoChavePix);
        Assert.Equal("novo@exemplo.com", dadosPix.ChavePix);
        Assert.True(dadosPix.UpdatedAt >= updatedAtAnterior);
    }

    [Fact]
    public void Reidratar_QuandoEstadoPersistidoForValidoDevePreservarCamposETimestamps()
    {
        var id = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddDays(2);

        var dadosPix = DadosPix.Reidratar(
            id,
            usuarioId,
            TipoChavePix.Email,
            "Snapshot.Exato@Exemplo.Com",
            createdAt,
            updatedAt);

        Assert.Equal(id, dadosPix.Id);
        Assert.Equal(usuarioId, dadosPix.UsuarioId);
        Assert.Equal(TipoChavePix.Email, dadosPix.TipoChavePix);
        Assert.Equal("Snapshot.Exato@Exemplo.Com", dadosPix.ChavePix);
        Assert.Equal(createdAt, dadosPix.CreatedAt);
        Assert.Equal(updatedAt, dadosPix.UpdatedAt);
    }

    [Fact]
    public void Reidratar_QuandoEstadoEstruturalForInvalidoDeveLancarExcecao()
    {
        var createdAt = new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => DadosPix.Reidratar(
            Guid.Empty, Guid.NewGuid(), TipoChavePix.Email, "pix@exemplo.com", createdAt, createdAt));
        Assert.Throws<ArgumentException>(() => DadosPix.Reidratar(
            Guid.NewGuid(), Guid.Empty, TipoChavePix.Email, "pix@exemplo.com", createdAt, createdAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => DadosPix.Reidratar(
            Guid.NewGuid(), Guid.NewGuid(), (TipoChavePix)99, "pix@exemplo.com", createdAt, createdAt));
        Assert.Throws<ArgumentException>(() => DadosPix.Reidratar(
            Guid.NewGuid(), Guid.NewGuid(), TipoChavePix.Email, " ", createdAt, createdAt));
        Assert.Throws<ArgumentException>(() => DadosPix.Reidratar(
            Guid.NewGuid(), Guid.NewGuid(), TipoChavePix.Email, "pix@exemplo.com", createdAt, createdAt.AddTicks(-1)));
    }

    private static DadosPix Criar(TipoChavePix tipoChavePix, string chavePix) =>
        new(Guid.NewGuid(), tipoChavePix, chavePix);
}

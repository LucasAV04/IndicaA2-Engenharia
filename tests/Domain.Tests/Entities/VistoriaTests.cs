using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class VistoriaTests
{
    [Fact]
    public void Construtor_QuandoDadosValidos_DeveCriarVistoriaAgendadaENormalizarTipoPlanta()
    {
        var usuarioId = Guid.NewGuid();
        var dataAgendada = new DateTime(2026, 9, 15, 9, 0, 0);

        var vistoria = new Vistoria(usuarioId, "  Apartamento  ", 72.5m, PacoteVistoria.Total, dataAgendada);

        Assert.Equal(usuarioId, vistoria.UsuarioId);
        Assert.Equal("Apartamento", vistoria.TipoPlanta);
        Assert.Equal(72.5m, vistoria.AreaM2);
        Assert.Equal(PacoteVistoria.Total, vistoria.Pacote);
        Assert.Equal(dataAgendada, vistoria.DataAgendada);
        Assert.Equal(StatusVistoria.Agendada, vistoria.Status);
    }

    [Fact]
    public void Construtor_QuandoUsuarioIdForVazio_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() => new Vistoria(
            Guid.Empty,
            "Apartamento",
            70m,
            PacoteVistoria.Simples,
            DateTime.UtcNow));
    }

    [Fact]
    public void Construtor_QuandoTipoPlantaForVazio_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() => new Vistoria(
            Guid.NewGuid(),
            " ",
            70m,
            PacoteVistoria.Simples,
            DateTime.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construtor_QuandoAreaM2NaoForPositiva_DeveLancarDomainException(decimal areaM2)
    {
        Assert.Throws<DomainException>(() => new Vistoria(
            Guid.NewGuid(),
            "Apartamento",
            areaM2,
            PacoteVistoria.Simples,
            DateTime.UtcNow));
    }

    [Fact]
    public void Construtor_QuandoPacoteForInvalido_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() => new Vistoria(
            Guid.NewGuid(),
            "Apartamento",
            70m,
            (PacoteVistoria)99,
            DateTime.UtcNow));
    }

    [Fact]
    public void Construtor_QuandoDataAgendadaForPadrao_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() => new Vistoria(
            Guid.NewGuid(),
            "Apartamento",
            70m,
            PacoteVistoria.Simples,
            default));
    }

    [Fact]
    public void MarcarRealizada_QuandoAgendada_DeveAlterarStatusEUpdatedAt()
    {
        var vistoria = CriarVistoria();
        var updatedAtAnterior = vistoria.UpdatedAt;

        vistoria.MarcarRealizada();

        Assert.Equal(StatusVistoria.Realizada, vistoria.Status);
        Assert.True(vistoria.UpdatedAt >= updatedAtAnterior);
    }

    [Fact]
    public void Concluir_QuandoRealizada_DeveAlterarStatusEUpdatedAt()
    {
        var vistoria = CriarVistoria();
        vistoria.MarcarRealizada();
        var updatedAtAnterior = vistoria.UpdatedAt;

        vistoria.Concluir();

        Assert.Equal(StatusVistoria.Concluida, vistoria.Status);
        Assert.True(vistoria.UpdatedAt >= updatedAtAnterior);
    }

    [Fact]
    public void Cancelar_QuandoAgendada_DeveAlterarStatusEUpdatedAt()
    {
        var vistoria = CriarVistoria();
        var updatedAtAnterior = vistoria.UpdatedAt;

        vistoria.Cancelar();

        Assert.Equal(StatusVistoria.Cancelada, vistoria.Status);
        Assert.True(vistoria.UpdatedAt >= updatedAtAnterior);
    }

    [Fact]
    public void Concluir_QuandoAgendada_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() => CriarVistoria().Concluir());
    }

    [Fact]
    public void Cancelar_QuandoRealizadaOuConcluida_DeveLancarDomainException()
    {
        var realizada = CriarVistoria();
        realizada.MarcarRealizada();
        Assert.Throws<DomainException>(() => realizada.Cancelar());

        realizada.Concluir();
        Assert.Throws<DomainException>(() => realizada.Cancelar());
    }

    [Fact]
    public void EstadosFinais_QuandoReceberemTransicoesIncompativeis_DevemLancarDomainException()
    {
        var cancelada = CriarVistoria();
        cancelada.Cancelar();
        Assert.Throws<DomainException>(() => cancelada.MarcarRealizada());
        Assert.Throws<DomainException>(() => cancelada.Concluir());

        var concluida = CriarVistoria();
        concluida.MarcarRealizada();
        concluida.Concluir();
        Assert.Throws<DomainException>(() => concluida.MarcarRealizada());
    }

    [Fact]
    public void MarcarRealizada_QuandoJaRealizada_DeveSerIdempotente()
    {
        var vistoria = CriarVistoria();
        vistoria.MarcarRealizada();
        var updatedAt = vistoria.UpdatedAt;

        vistoria.MarcarRealizada();

        Assert.Equal(StatusVistoria.Realizada, vistoria.Status);
        Assert.Equal(updatedAt, vistoria.UpdatedAt);
    }

    [Fact]
    public void Concluir_QuandoJaConcluida_DeveSerIdempotente()
    {
        var vistoria = CriarVistoria();
        vistoria.MarcarRealizada();
        vistoria.Concluir();
        var updatedAt = vistoria.UpdatedAt;

        vistoria.Concluir();

        Assert.Equal(StatusVistoria.Concluida, vistoria.Status);
        Assert.Equal(updatedAt, vistoria.UpdatedAt);
    }

    [Fact]
    public void Cancelar_QuandoJaCancelada_DeveSerIdempotente()
    {
        var vistoria = CriarVistoria();
        vistoria.Cancelar();
        var updatedAt = vistoria.UpdatedAt;

        vistoria.Cancelar();

        Assert.Equal(StatusVistoria.Cancelada, vistoria.Status);
        Assert.Equal(updatedAt, vistoria.UpdatedAt);
    }

    private static Vistoria CriarVistoria() => new(
        Guid.NewGuid(),
        "Apartamento",
        70m,
        PacoteVistoria.Simples,
        DateTime.UtcNow);
}

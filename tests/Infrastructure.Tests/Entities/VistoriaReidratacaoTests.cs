using Domain.Entities;
using Domain.Enums;
using Xunit;

namespace Infrastructure.Tests.Entities;

public sealed class VistoriaReidratacaoTests
{
    [Fact]
    public void Reidratar_QuandoEstadoPersistidoValido_DevePreservarTodosOsCampos()
    {
        var id = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var dataAgendada = new DateTime(2026, 9, 15, 14, 30, 0, DateTimeKind.Unspecified);
        var createdAt = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(10);

        var vistoria = Vistoria.Reidratar(
            id,
            usuarioId,
            "Apartamento",
            85.50m,
            PacoteVistoria.Total,
            dataAgendada,
            StatusVistoria.Agendada,
            createdAt,
            updatedAt);

        Assert.Equal(id, vistoria.Id);
        Assert.Equal(usuarioId, vistoria.UsuarioId);
        Assert.Equal("Apartamento", vistoria.TipoPlanta);
        Assert.Equal(85.50m, vistoria.AreaM2);
        Assert.Equal(PacoteVistoria.Total, vistoria.Pacote);
        Assert.Equal(dataAgendada, vistoria.DataAgendada);
        Assert.Equal(StatusVistoria.Agendada, vistoria.Status);
        Assert.Equal(createdAt, vistoria.CreatedAt);
        Assert.Equal(updatedAt, vistoria.UpdatedAt);
    }

    [Theory]
    [InlineData(StatusVistoria.Realizada)]
    [InlineData(StatusVistoria.Concluida)]
    [InlineData(StatusVistoria.Cancelada)]
    public void Reidratar_QuandoStatusPersistidoForPosterior_DevePreservarStatus(StatusVistoria status)
    {
        var vistoria = CriarVistoria(status: status);

        Assert.Equal(status, vistoria.Status);
    }

    [Fact]
    public void Reidratar_QuandoIdentificadorForVazio_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CriarVistoria(id: Guid.Empty));
    }

    [Fact]
    public void Reidratar_QuandoUsuarioIdForVazio_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CriarVistoria(usuarioId: Guid.Empty));
    }

    [Fact]
    public void Reidratar_QuandoPacoteForInvalido_DeveLancarArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CriarVistoria(pacote: (PacoteVistoria)99));
    }

    [Fact]
    public void Reidratar_QuandoStatusForInvalido_DeveLancarArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CriarVistoria(status: (StatusVistoria)99));
    }

    [Fact]
    public void Reidratar_QuandoUpdatedAtForAnteriorACreatedAt_DeveLancarArgumentException()
    {
        var createdAt = DateTime.UtcNow;

        Assert.Throws<ArgumentException>(() => CriarVistoria(
            createdAt: createdAt,
            updatedAt: createdAt.AddTicks(-1)));
    }

    private static Vistoria CriarVistoria(
        Guid? id = null,
        Guid? usuarioId = null,
        PacoteVistoria pacote = PacoteVistoria.Simples,
        StatusVistoria status = StatusVistoria.Agendada,
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        var dataCriacao = createdAt ?? DateTime.UtcNow;

        return Vistoria.Reidratar(
            id ?? Guid.NewGuid(),
            usuarioId ?? Guid.NewGuid(),
            "Casa",
            70m,
            pacote,
            new DateTime(2026, 9, 15, 14, 30, 0, DateTimeKind.Unspecified),
            status,
            dataCriacao,
            updatedAt ?? dataCriacao);
    }
}

using Domain.Entities;
using Domain.Enums;
using Xunit;

namespace Infrastructure.Tests.Entities;

public sealed class IndicacaoReidratacaoTests
{
    [Fact]
    public void Reidratar_QuandoEstadoPersistidoValido_DevePreservarCamposEDatas()
    {
        var id = Guid.NewGuid();
        var indicadorId = Guid.NewGuid();
        var indicadoId = Guid.NewGuid();
        var vistoriaId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(5);

        var indicacao = Indicacao.Reidratar(
            id,
            indicadorId,
            indicadoId,
            "Ana Indicada",
            "11999999999",
            "A2-123",
            vistoriaId,
            StatusIndicacao.VistoriaConcluida,
            createdAt,
            updatedAt);

        Assert.Equal(id, indicacao.Id);
        Assert.Equal(indicadorId, indicacao.UsuarioIndicadorId);
        Assert.Equal(indicadoId, indicacao.UsuarioIndicadoId);
        Assert.Equal(vistoriaId, indicacao.VistoriaId);
        Assert.Equal(StatusIndicacao.VistoriaConcluida, indicacao.Status);
        Assert.Equal(createdAt, indicacao.CreatedAt);
        Assert.Equal(updatedAt, indicacao.UpdatedAt);
    }

    [Fact]
    public void Reidratar_QuandoStatusExigeVistoriaSemVistoria_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Indicacao.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Ana Indicada",
            "11999999999",
            "A2-123",
            null,
            StatusIndicacao.VistoriaVinculada,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Fact]
    public void Reidratar_QuandoUsuarioIndicadoForGuidEmpty_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Indicacao.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            "Ana Indicada",
            "11999999999",
            "A2-123",
            null,
            StatusIndicacao.Pendente,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Fact]
    public void Reidratar_QuandoVistoriaForGuidEmpty_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Indicacao.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Ana Indicada",
            "11999999999",
            "A2-123",
            Guid.Empty,
            StatusIndicacao.VistoriaVinculada,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Fact]
    public void Reidratar_QuandoStatusPendentePossuirVistoria_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Indicacao.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Ana Indicada",
            "11999999999",
            "A2-123",
            Guid.NewGuid(),
            StatusIndicacao.Pendente,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Fact]
    public void Reidratar_QuandoCanceladaPossuirVistoria_DevePreservarEstado()
    {
        var vistoriaId = Guid.NewGuid();

        var indicacao = Indicacao.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Ana Indicada",
            "11999999999",
            "A2-123",
            vistoriaId,
            StatusIndicacao.Cancelada,
            DateTime.UtcNow,
            DateTime.UtcNow);

        Assert.Equal(StatusIndicacao.Cancelada, indicacao.Status);
        Assert.Equal(vistoriaId, indicacao.VistoriaId);
    }
}

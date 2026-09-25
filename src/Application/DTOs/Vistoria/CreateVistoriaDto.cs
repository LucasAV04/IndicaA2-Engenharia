using Domain.Enums;

namespace Application.DTOs.Vistoria;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateVistoriaDto
{
    public Guid UsuarioId { get; set; }

    public Guid TipoPlantaId { get; set; }

    public decimal AreaM2 { get; set; }

    public PacoteVistoria Pacote { get; set; }

    public DateTime DataAgendada { get; set; }
}

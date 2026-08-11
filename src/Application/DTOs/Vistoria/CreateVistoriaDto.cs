using Domain.Enums;

namespace Application.DTOs.Vistoria;

public sealed class CreateVistoriaDto
{
    public Guid UsuarioId { get; set; }

    public string TipoPlanta { get; set; } = string.Empty;

    public decimal AreaM2 { get; set; }

    public PacoteVistoria Pacote { get; set; }

    public DateTime DataAgendada { get; set; }
}

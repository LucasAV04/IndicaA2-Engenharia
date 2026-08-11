using Domain.Enums;

namespace Application.DTOs.Vistoria;

public sealed class VistoriaResponseDto
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }

    public string TipoPlanta { get; set; } = string.Empty;

    public decimal AreaM2 { get; set; }

    public PacoteVistoria Pacote { get; set; }

    public DateTime DataAgendada { get; set; }

    public StatusVistoria Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

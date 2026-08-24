using Domain.Enums;

namespace Application.DTOs.DadosPix;

public sealed class DadosPixResponseDto
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }
    public TipoChavePix TipoChavePix { get; set; }
    public string ChavePix { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

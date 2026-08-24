using Domain.Enums;

namespace Application.DTOs.DadosPix;

public sealed class DadosPixDto
{
    public TipoChavePix TipoChavePix { get; set; }
    public string ChavePix { get; set; } = string.Empty;
}

using Application.DTOs.Vistoria;
using Domain.Entities;

namespace Application.Mapping;

public static class VistoriaMapper
{
    public static VistoriaResponseDto ToResponseDto(this Vistoria vistoria) => new()
    {
        Id = vistoria.Id,
        UsuarioId = vistoria.UsuarioId,
        TipoPlanta = vistoria.TipoPlanta,
        AreaM2 = vistoria.AreaM2,
        Pacote = vistoria.Pacote,
        DataAgendada = vistoria.DataAgendada,
        Status = vistoria.Status,
        CreatedAt = vistoria.CreatedAt,
        UpdatedAt = vistoria.UpdatedAt
    };

    public static IReadOnlyCollection<VistoriaResponseDto> ToResponseDto(
        this IEnumerable<Vistoria> vistorias) =>
        vistorias.Select(ToResponseDto).ToArray();
}

using Application.DTOs.DadosPix;
using Domain.Entities;

namespace Application.Mapping;

public static class DadosPixMapper
{
    public static DadosPixResponseDto ToResponseDto(this DadosPix dadosPix) => new()
    {
        Id = dadosPix.Id,
        UsuarioId = dadosPix.UsuarioId,
        TipoChavePix = dadosPix.TipoChavePix,
        ChavePix = dadosPix.ChavePix,
        CreatedAt = dadosPix.CreatedAt,
        UpdatedAt = dadosPix.UpdatedAt
    };
}

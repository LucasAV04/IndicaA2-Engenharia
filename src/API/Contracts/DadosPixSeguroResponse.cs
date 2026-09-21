using Application.DTOs.DadosPix;
using Domain.Enums;

namespace API.Contracts;

public sealed record DadosPixSeguroResponse(
    Guid Id, Guid UsuarioId, TipoChavePix TipoChavePix, string ChaveMascarada,
    DateTime CreatedAt, DateTime UpdatedAt)
{
    public static DadosPixSeguroResponse From(DadosPixResponseDto dto)
    {
        var chave = dto.ChavePix;
        var mascara = dto.TipoChavePix switch
        {
            TipoChavePix.Email => chave.Contains('@') ? "***" + chave[chave.LastIndexOf('@')..] : "***",
            TipoChavePix.Aleatoria => "••••••••",
            _ => "••••" + chave[Math.Max(0, chave.Length - 4)..]
        };
        return new(dto.Id, dto.UsuarioId, dto.TipoChavePix, mascara, dto.CreatedAt, dto.UpdatedAt);
    }
}

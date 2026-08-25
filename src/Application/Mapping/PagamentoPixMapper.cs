using Application.DTOs.PagamentoPix;
using Domain.Entities;

namespace Application.Mapping;

public static class PagamentoPixMapper
{
    public static PagamentoPixResponseDto ToResponseDto(this PagamentoPix pagamentoPix) => new()
    {
        Id = pagamentoPix.Id,
        CashbackId = pagamentoPix.CashbackId,
        UsuarioBeneficiarioId = pagamentoPix.UsuarioBeneficiarioId,
        Valor = pagamentoPix.Valor,
        TipoChavePix = pagamentoPix.TipoChavePix,
        Status = pagamentoPix.Status,
        QuantidadeTentativas = pagamentoPix.QuantidadeTentativas,
        CreatedAt = pagamentoPix.CreatedAt,
        UpdatedAt = pagamentoPix.UpdatedAt
    };

    public static IReadOnlyCollection<PagamentoPixResponseDto> ToResponseDto(
        this IEnumerable<PagamentoPix> pagamentosPix) =>
        pagamentosPix.Select(ToResponseDto).ToArray();
}

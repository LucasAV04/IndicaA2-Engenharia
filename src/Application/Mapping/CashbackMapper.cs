using Application.DTOs.Cashback;
using Domain.Entities;

namespace Application.Mapping;

public static class CashbackMapper
{
    public static CashbackResponseDto ToResponseDto(this Cashback cashback) => new()
    {
        Id = cashback.Id,
        IndicacaoId = cashback.IndicacaoId,
        PagamentoVistoriaId = cashback.PagamentoVistoriaId,
        UsuarioIndicadorId = cashback.UsuarioIndicadorId,
        ValorTotalPago = cashback.ValorTotalPago,
        Percentual = cashback.Percentual,
        Valor = cashback.Valor,
        Status = cashback.Status,
        CreatedAt = cashback.CreatedAt,
        UpdatedAt = cashback.UpdatedAt
    };

    public static IReadOnlyCollection<CashbackResponseDto> ToResponseDto(
        this IEnumerable<Cashback> cashbacks) =>
        cashbacks.Select(ToResponseDto).ToArray();
}

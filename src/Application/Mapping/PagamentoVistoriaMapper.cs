using Application.DTOs.PagamentoVistoria;
using Domain.Entities;

namespace Application.Mapping;

public static class PagamentoVistoriaMapper
{
    public static PagamentoVistoriaResponseDto ToResponseDto(this PagamentoVistoria pagamentoVistoria) => new()
    {
        Id = pagamentoVistoria.Id,
        VistoriaId = pagamentoVistoria.VistoriaId,
        Valor = pagamentoVistoria.Valor,
        Status = pagamentoVistoria.Status,
        PagoEm = pagamentoVistoria.PagoEm,
        CreatedAt = pagamentoVistoria.CreatedAt,
        UpdatedAt = pagamentoVistoria.UpdatedAt
    };

    public static IReadOnlyCollection<PagamentoVistoriaResponseDto> ToResponseDto(
        this IEnumerable<PagamentoVistoria> pagamentosVistoria) =>
        pagamentosVistoria.Select(ToResponseDto).ToArray();
}

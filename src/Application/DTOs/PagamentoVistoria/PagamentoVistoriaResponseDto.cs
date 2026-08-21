using Domain.Enums;

namespace Application.DTOs.PagamentoVistoria;

public sealed class PagamentoVistoriaResponseDto
{
    public Guid Id { get; set; }

    public Guid VistoriaId { get; set; }

    public decimal Valor { get; set; }

    public StatusPagamentoVistoria Status { get; set; }

    public DateTime? PagoEm { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

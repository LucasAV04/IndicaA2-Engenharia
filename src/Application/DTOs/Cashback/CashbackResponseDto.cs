using Domain.Enums;

namespace Application.DTOs.Cashback;

public sealed class CashbackResponseDto
{
    public Guid Id { get; set; }

    public Guid IndicacaoId { get; set; }

    public Guid PagamentoVistoriaId { get; set; }

    public Guid UsuarioIndicadorId { get; set; }

    public decimal ValorTotalPago { get; set; }

    public decimal Percentual { get; set; }

    public decimal Valor { get; set; }

    public StatusCashback Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

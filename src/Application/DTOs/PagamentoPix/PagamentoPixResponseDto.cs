using Domain.Enums;

namespace Application.DTOs.PagamentoPix;

public sealed class PagamentoPixResponseDto
{
    public Guid Id { get; set; }

    public Guid CashbackId { get; set; }

    public Guid UsuarioBeneficiarioId { get; set; }

    public decimal Valor { get; set; }

    public TipoChavePix TipoChavePix { get; set; }

    public StatusPagamentoPix Status { get; set; }

    public int QuantidadeTentativas { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

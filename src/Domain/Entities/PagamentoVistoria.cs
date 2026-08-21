using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class PagamentoVistoria : BaseEntity
{
    public Guid VistoriaId { get; private set; }

    public decimal Valor { get; private set; }

    public StatusPagamentoVistoria Status { get; private set; }

    public DateTime? PagoEm { get; private set; }

    private PagamentoVistoria()
    {
    }

    public PagamentoVistoria(Guid vistoriaId, decimal valor)
    {
        if (vistoriaId == Guid.Empty)
            throw new DomainException("O identificador da vistoria é obrigatório.");

        var valorNormalizado = decimal.Round(valor, 2, MidpointRounding.AwayFromZero);

        if (valorNormalizado <= 0)
            throw new DomainException("O valor do pagamento deve ser maior que zero.");

        VistoriaId = vistoriaId;
        Valor = valorNormalizado;
        Status = StatusPagamentoVistoria.Pendente;
        PagoEm = null;
    }

    internal static PagamentoVistoria Reidratar(
        Guid id,
        Guid vistoriaId,
        decimal valor,
        StatusPagamentoVistoria status,
        DateTime? pagoEm,
        DateTime createdAt,
        DateTime updatedAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador persistido é obrigatório.", nameof(id));
        if (vistoriaId == Guid.Empty)
            throw new ArgumentException("O identificador da vistoria persistida é obrigatório.", nameof(vistoriaId));
        if (valor <= 0)
            throw new ArgumentOutOfRangeException(nameof(valor), "O valor persistido deve ser maior que zero.");
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), "O status persistido é inválido.");
        if (createdAt == default)
            throw new ArgumentException("A data de criação persistida é obrigatória.", nameof(createdAt));
        if (updatedAt == default)
            throw new ArgumentException("A data de atualização persistida é obrigatória.", nameof(updatedAt));
        if (updatedAt < createdAt)
            throw new ArgumentException("A data de atualização não pode ser anterior à data de criação.", nameof(updatedAt));
        if (status == StatusPagamentoVistoria.Confirmado && pagoEm is null)
            throw new ArgumentException("Pagamento confirmado deve possuir data de pagamento persistida.", nameof(pagoEm));
        if (status != StatusPagamentoVistoria.Confirmado && pagoEm is not null)
            throw new ArgumentException("Pagamento não confirmado não pode possuir data de pagamento persistida.", nameof(pagoEm));

        return new PagamentoVistoria
        {
            Id = id,
            VistoriaId = vistoriaId,
            Valor = valor,
            Status = status,
            PagoEm = pagoEm,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void Confirmar()
    {
        if (Status == StatusPagamentoVistoria.Confirmado)
            return;

        if (Status != StatusPagamentoVistoria.Pendente)
            throw new DomainException("Apenas pagamentos pendentes podem ser confirmados.");

        Status = StatusPagamentoVistoria.Confirmado;
        PagoEm = DateTime.UtcNow;
        AtualizarDataAlteracao();
    }

    public void Cancelar()
    {
        if (Status == StatusPagamentoVistoria.Cancelado)
            return;

        if (Status != StatusPagamentoVistoria.Pendente)
            throw new DomainException("Apenas pagamentos pendentes podem ser cancelados.");

        Status = StatusPagamentoVistoria.Cancelado;
        AtualizarDataAlteracao();
    }
}

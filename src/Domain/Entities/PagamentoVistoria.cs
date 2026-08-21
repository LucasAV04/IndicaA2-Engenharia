using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class PagamentoVistoria : BaseEntity
{
    public Guid VistoriaId { get; private set; }

    public decimal Valor { get; private set; }

    public StatusPagamentoVistoria Status { get; private set; }

    public DateTime? PagoEm { get; private set; }

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

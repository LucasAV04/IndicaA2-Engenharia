using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class Cashback : BaseEntity
{
    private const decimal PercentualCashback = 0.20m;

    public Guid IndicacaoId { get; private set; }

    public Guid PagamentoVistoriaId { get; private set; }

    public Guid UsuarioIndicadorId { get; private set; }

    public decimal ValorTotalPago { get; private set; }

    public decimal Percentual { get; private set; }

    public decimal Valor { get; private set; }

    public StatusCashback Status { get; private set; }

    private Cashback()
    {
    }

    public static Cashback Criar(
        Guid indicacaoId,
        Guid pagamentoVistoriaId,
        Guid usuarioIndicadorId,
        decimal valorTotalPago)
    {
        if (indicacaoId == Guid.Empty)
            throw new DomainException("O identificador da indicação é obrigatório.");
        if (pagamentoVistoriaId == Guid.Empty)
            throw new DomainException("O identificador do pagamento da vistoria é obrigatório.");
        if (usuarioIndicadorId == Guid.Empty)
            throw new DomainException("O usuário indicador é obrigatório.");

        var valorTotalPagoNormalizado = decimal.Round(
            valorTotalPago,
            2,
            MidpointRounding.AwayFromZero);

        if (valorTotalPagoNormalizado <= 0)
            throw new DomainException("O valor total pago deve ser maior que zero.");

        return new Cashback
        {
            IndicacaoId = indicacaoId,
            PagamentoVistoriaId = pagamentoVistoriaId,
            UsuarioIndicadorId = usuarioIndicadorId,
            ValorTotalPago = valorTotalPagoNormalizado,
            Percentual = PercentualCashback,
            Valor = decimal.Round(
                valorTotalPagoNormalizado * PercentualCashback,
                2,
                MidpointRounding.AwayFromZero),
            Status = StatusCashback.Pendente
        };
    }

    public void Aprovar()
    {
        if (Status == StatusCashback.Disponivel)
            return;

        GarantirStatus(StatusCashback.Pendente, "aprovar o cashback");
        Status = StatusCashback.Disponivel;
        AtualizarDataAlteracao();
    }

    public void Cancelar()
    {
        if (Status == StatusCashback.Cancelado)
            return;

        if (Status is not (StatusCashback.Pendente or StatusCashback.Disponivel))
        {
            throw new DomainException(
                $"Não é possível cancelar o cashback: status atual é '{Status}'.");
        }

        Status = StatusCashback.Cancelado;
        AtualizarDataAlteracao();
    }

    private void GarantirStatus(StatusCashback statusEsperado, string acao)
    {
        if (Status != statusEsperado)
        {
            throw new DomainException(
                $"Não é possível {acao}: status atual é '{Status}', esperado '{statusEsperado}'.");
        }
    }
}

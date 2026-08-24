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

    internal static Cashback Reidratar(
        Guid id,
        Guid indicacaoId,
        Guid pagamentoVistoriaId,
        Guid usuarioIndicadorId,
        decimal valorTotalPago,
        decimal percentual,
        decimal valor,
        StatusCashback status,
        DateTime createdAt,
        DateTime updatedAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O identificador persistido é obrigatório.", nameof(id));
        if (indicacaoId == Guid.Empty)
            throw new ArgumentException("O identificador da indicação persistida é obrigatório.", nameof(indicacaoId));
        if (pagamentoVistoriaId == Guid.Empty)
            throw new ArgumentException("O identificador do pagamento da vistoria persistido é obrigatório.", nameof(pagamentoVistoriaId));
        if (usuarioIndicadorId == Guid.Empty)
            throw new ArgumentException("O usuário indicador persistido é obrigatório.", nameof(usuarioIndicadorId));
        if (valorTotalPago <= 0)
            throw new ArgumentOutOfRangeException(nameof(valorTotalPago), "O valor total pago persistido deve ser maior que zero.");
        if (percentual <= 0)
            throw new ArgumentOutOfRangeException(nameof(percentual), "O percentual persistido deve ser maior que zero.");
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

        return new Cashback
        {
            Id = id,
            IndicacaoId = indicacaoId,
            PagamentoVistoriaId = pagamentoVistoriaId,
            UsuarioIndicadorId = usuarioIndicadorId,
            ValorTotalPago = valorTotalPago,
            Percentual = percentual,
            Valor = valor,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
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

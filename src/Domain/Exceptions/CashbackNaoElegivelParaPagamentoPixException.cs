namespace Domain.Exceptions;

public sealed class CashbackNaoElegivelParaPagamentoPixException() : DomainException(
    "Apenas cashback disponível pode gerar uma ordem de Pagamento Pix.");

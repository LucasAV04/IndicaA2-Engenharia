namespace Domain.Exceptions;

public sealed class PagamentoPixJaExisteException() : DomainException(
    "Já existe uma ordem de Pagamento Pix para este cashback.");

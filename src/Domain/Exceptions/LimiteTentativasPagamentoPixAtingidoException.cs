namespace Domain.Exceptions;

public sealed class LimiteTentativasPagamentoPixAtingidoException() : DomainException(
    "O limite de tentativas do Pagamento Pix foi atingido.");

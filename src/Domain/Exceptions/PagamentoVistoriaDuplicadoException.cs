namespace Domain.Exceptions;

public sealed class PagamentoVistoriaDuplicadoException() : DomainException(
    "Já existe um pagamento registrado para esta vistoria.");

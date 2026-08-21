namespace Domain.Exceptions;

public sealed class CashbackJaExisteException() : DomainException(
    "Já existe um cashback registrado para este pagamento de vistoria.");

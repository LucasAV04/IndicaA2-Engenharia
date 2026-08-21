namespace Domain.Exceptions;

public sealed class CashbackNaoEncontradoException() : DomainException(
    "Cashback não encontrado.");

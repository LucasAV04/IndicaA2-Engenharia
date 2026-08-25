namespace Domain.Exceptions;

public sealed class DadosPixJaExisteException() : DomainException(
    "Já existem Dados Pix cadastrados para este usuário.");

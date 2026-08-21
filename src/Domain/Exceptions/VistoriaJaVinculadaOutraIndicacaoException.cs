namespace Domain.Exceptions;

public sealed class VistoriaJaVinculadaOutraIndicacaoException() : DomainException(
    "Esta vistoria já está vinculada a outra indicação.");

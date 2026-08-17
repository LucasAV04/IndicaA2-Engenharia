namespace Domain.Exceptions.Usuario;

public sealed class CodigoIndicacaoDuplicadoException() : DomainException(
    "O código de indicação informado já está em uso.");

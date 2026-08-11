namespace Domain.Exceptions;

public sealed class CredenciaisInvalidasException : DomainException
{
    public CredenciaisInvalidasException()
        : base("E-mail ou senha inválidos.")
    {
    }
}

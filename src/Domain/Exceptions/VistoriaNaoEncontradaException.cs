namespace Domain.Exceptions;

public sealed class VistoriaNaoEncontradaException : DomainException
{
    public VistoriaNaoEncontradaException()
        : base("Vistoria não encontrada.")
    {
    }
}

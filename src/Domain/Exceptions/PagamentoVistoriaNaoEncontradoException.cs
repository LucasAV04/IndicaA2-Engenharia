namespace Domain.Exceptions;

public sealed class PagamentoVistoriaNaoEncontradoException : DomainException
{
    public PagamentoVistoriaNaoEncontradoException()
        : base("Pagamento da vistoria não encontrado.")
    {
    }
}

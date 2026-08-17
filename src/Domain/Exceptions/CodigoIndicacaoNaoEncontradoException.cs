namespace Domain.Exceptions
{
    public sealed class CodigoIndicacaoNaoEncontradoException : DomainException
    {
        public CodigoIndicacaoNaoEncontradoException()
            : base("Código de indicação não encontrado.")
        {
        }
    }
}

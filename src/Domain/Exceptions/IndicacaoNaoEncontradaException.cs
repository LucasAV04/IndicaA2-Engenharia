namespace Domain.Exceptions
{
    public sealed class IndicacaoNaoEncontradaException : DomainException
    {
        public IndicacaoNaoEncontradaException()
            : base("Indicação não encontrada.")
        {
        }
    }
}

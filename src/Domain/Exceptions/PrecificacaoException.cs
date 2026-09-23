namespace Domain.Exceptions;

public sealed class PrecificacaoException : DomainException
{
    public string Codigo { get; }
    public PrecificacaoException(string codigo) : base("Não foi possível concluir a operação de precificação.") => Codigo = codigo;
}

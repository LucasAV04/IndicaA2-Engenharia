namespace Domain.Exceptions;

public sealed class VistoriaLegadaSemPrecificacaoException : DomainException
{
    public VistoriaLegadaSemPrecificacaoException()
        : base("Vistoria legada sem snapshot: aguarde regularização administrativa futura antes de criar pagamento.") { }
}

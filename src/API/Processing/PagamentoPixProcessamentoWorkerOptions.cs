namespace API.Processing;

public sealed class PagamentoPixProcessamentoWorkerOptions
{
    public const string SectionName = "PagamentoPix:ProcessamentoWorker";

    public bool Habilitado { get; init; }
    public int IntervaloSegundos { get; init; } = 30;
    public int TamanhoLote { get; init; } = 20;

    public void Validate()
    {
        if (!Habilitado)
            return;
        if (IntervaloSegundos is < 5 or > 3600)
            throw new InvalidOperationException("PagamentoPix:ProcessamentoWorker:IntervaloSegundos deve estar entre 5 e 3600 quando o worker estiver habilitado.");
        if (TamanhoLote is < 1 or > 100)
            throw new InvalidOperationException("PagamentoPix:ProcessamentoWorker:TamanhoLote deve estar entre 1 e 100 quando o worker estiver habilitado.");
    }
}

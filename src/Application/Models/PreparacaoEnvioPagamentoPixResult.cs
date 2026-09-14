namespace Application.Models;

/// <summary>
/// Resultado seguro da preparação transacional de uma tentativa de envio Pix.
/// Não contém snapshots, chaves Pix ou dados do provider.
/// </summary>
public sealed class PreparacaoEnvioPagamentoPixResult
{
    private PreparacaoEnvioPagamentoPixResult(
        bool adquirido,
        Guid? operacaoPagamentoPixId,
        int? numeroTentativaEnvio,
        Guid? leaseId,
        string? referenciaIdempotente)
    {
        Adquirido = adquirido;
        OperacaoPagamentoPixId = operacaoPagamentoPixId;
        NumeroTentativaEnvio = numeroTentativaEnvio;
        LeaseId = leaseId;
        ReferenciaIdempotente = referenciaIdempotente;
    }

    public bool Adquirido { get; }
    public Guid? OperacaoPagamentoPixId { get; }
    public int? NumeroTentativaEnvio { get; }
    public Guid? LeaseId { get; }
    public string? ReferenciaIdempotente { get; }

    public static PreparacaoEnvioPagamentoPixResult NaoAdquirido() => new(false, null, null, null, null);

    public static PreparacaoEnvioPagamentoPixResult AdquiridoCom(
        Guid operacaoPagamentoPixId,
        int numeroTentativaEnvio,
        Guid leaseId,
        string referenciaIdempotente)
    {
        if (operacaoPagamentoPixId == Guid.Empty)
            throw new ArgumentException("O identificador da operação é obrigatório.", nameof(operacaoPagamentoPixId));
        if (numeroTentativaEnvio <= 0)
            throw new ArgumentOutOfRangeException(nameof(numeroTentativaEnvio));
        if (leaseId == Guid.Empty)
            throw new ArgumentException("O token do lease é obrigatório.", nameof(leaseId));
        if (string.IsNullOrWhiteSpace(referenciaIdempotente))
            throw new ArgumentException("A referência idempotente é obrigatória.", nameof(referenciaIdempotente));

        return new(true, operacaoPagamentoPixId, numeroTentativaEnvio, leaseId, referenciaIdempotente);
    }
}

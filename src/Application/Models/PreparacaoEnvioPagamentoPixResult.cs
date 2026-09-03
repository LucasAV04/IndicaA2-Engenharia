namespace Application.Models;

/// <summary>
/// Resultado seguro da preparação transacional de uma tentativa de envio Pix.
/// Não contém snapshots, chaves Pix ou dados do provider.
/// </summary>
public sealed class PreparacaoEnvioPagamentoPixResult
{
    private PreparacaoEnvioPagamentoPixResult(bool adquirido, Guid? operacaoPagamentoPixId, int? numeroTentativaEnvio)
    {
        Adquirido = adquirido;
        OperacaoPagamentoPixId = operacaoPagamentoPixId;
        NumeroTentativaEnvio = numeroTentativaEnvio;
    }

    public bool Adquirido { get; }
    public Guid? OperacaoPagamentoPixId { get; }
    public int? NumeroTentativaEnvio { get; }

    public static PreparacaoEnvioPagamentoPixResult NaoAdquirido() => new(false, null, null);

    public static PreparacaoEnvioPagamentoPixResult AdquiridoCom(Guid operacaoPagamentoPixId, int numeroTentativaEnvio)
    {
        if (operacaoPagamentoPixId == Guid.Empty)
            throw new ArgumentException("O identificador da operação é obrigatório.", nameof(operacaoPagamentoPixId));
        if (numeroTentativaEnvio <= 0)
            throw new ArgumentOutOfRangeException(nameof(numeroTentativaEnvio));

        return new(true, operacaoPagamentoPixId, numeroTentativaEnvio);
    }
}

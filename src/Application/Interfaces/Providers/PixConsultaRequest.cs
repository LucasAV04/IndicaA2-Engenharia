namespace Application.Interfaces.Providers;

/// <summary>
/// Contrato interno para reconciliação de uma ordem Pix pela mesma referência idempotente do envio.
/// </summary>
public sealed class PixConsultaRequest
{
    public PixConsultaRequest(Guid pagamentoPixId)
    {
        PagamentoPixId = pagamentoPixId;
        ReferenciaIdempotente = PixReferenciaIdempotente.Criar(pagamentoPixId);
    }

    public Guid PagamentoPixId { get; }

    public string ReferenciaIdempotente { get; }
}

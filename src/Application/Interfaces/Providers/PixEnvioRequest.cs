using Domain.Enums;

namespace Application.Interfaces.Providers;

/// <summary>
/// Contrato interno para solicitar o envio de uma ordem Pix já resolvida pela Application.
/// Não é um DTO público e não deve ser registrado em logs.
/// </summary>
public sealed class PixEnvioRequest
{
    public PixEnvioRequest(
        Guid pagamentoPixId,
        decimal valor,
        TipoChavePix tipoChavePix,
        string chavePix)
    {
        if (valor <= 0)
            throw new ArgumentOutOfRangeException(nameof(valor), "O valor do Pix deve ser maior que zero.");
        if (!Enum.IsDefined(tipoChavePix))
            throw new ArgumentOutOfRangeException(nameof(tipoChavePix), "O tipo de chave Pix é inválido.");
        if (string.IsNullOrWhiteSpace(chavePix))
            throw new ArgumentException("A chave Pix é obrigatória.", nameof(chavePix));

        PagamentoPixId = pagamentoPixId;
        ReferenciaIdempotente = PixReferenciaIdempotente.Criar(pagamentoPixId);
        Valor = valor;
        TipoChavePix = tipoChavePix;
        ChavePix = chavePix;
    }

    public Guid PagamentoPixId { get; }

    public string ReferenciaIdempotente { get; }

    public decimal Valor { get; }

    public TipoChavePix TipoChavePix { get; }

    public string ChavePix { get; }

    public override string ToString() =>
        $"{nameof(PixEnvioRequest)} {{ {nameof(PagamentoPixId)} = {PagamentoPixId}, " +
        $"{nameof(ReferenciaIdempotente)} = {ReferenciaIdempotente}, " +
        $"{nameof(Valor)} = {Valor}, {nameof(TipoChavePix)} = {TipoChavePix} }}";
}

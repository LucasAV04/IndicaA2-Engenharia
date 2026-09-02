using Domain.Enums;

namespace Application.Models;

/// <summary>
/// Resultado seguro da orquestração. Não expõe Dados Pix ou detalhes técnicos do provider.
/// </summary>
public sealed class ResultadoEnvioPagamentoPix
{
    private ResultadoEnvioPagamentoPix(
        Guid pagamentoPixId,
        bool envioExecutado,
        Guid? operacaoPagamentoPixId,
        int? numeroTentativaEnvio,
        ResultadoOperacaoPagamentoPix? resultadoOperacao)
    {
        PagamentoPixId = pagamentoPixId;
        EnvioExecutado = envioExecutado;
        OperacaoPagamentoPixId = operacaoPagamentoPixId;
        NumeroTentativaEnvio = numeroTentativaEnvio;
        ResultadoOperacao = resultadoOperacao;
    }

    public Guid PagamentoPixId { get; }
    public bool EnvioExecutado { get; }
    public Guid? OperacaoPagamentoPixId { get; }
    public int? NumeroTentativaEnvio { get; }
    public ResultadoOperacaoPagamentoPix? ResultadoOperacao { get; }

    public static ResultadoEnvioPagamentoPix NaoAdquirido(Guid pagamentoPixId) =>
        new(pagamentoPixId, false, null, null, null);

    public static ResultadoEnvioPagamentoPix Executado(
        Guid pagamentoPixId,
        Guid operacaoPagamentoPixId,
        int numeroTentativaEnvio,
        ResultadoOperacaoPagamentoPix resultadoOperacao) =>
        new(pagamentoPixId, true, operacaoPagamentoPixId, numeroTentativaEnvio, resultadoOperacao);
}

using Domain.Enums;

namespace Application.Models;

/// <summary>
/// Resultado seguro da reconciliação. Não expõe chave Pix, payloads ou detalhes
/// técnicos do provider.
/// </summary>
public sealed class ResultadoReconciliacaoPagamentoPix
{
    private ResultadoReconciliacaoPagamentoPix(
        Guid pagamentoPixId,
        StatusReconciliacaoPagamentoPix status,
        Guid? operacaoConsultaId,
        ResultadoOperacaoPagamentoPix? resultadoOperacao,
        bool operacaoEnvioAbertaResolvida)
    {
        PagamentoPixId = pagamentoPixId;
        Status = status;
        OperacaoConsultaId = operacaoConsultaId;
        ResultadoOperacao = resultadoOperacao;
        OperacaoEnvioAbertaResolvida = operacaoEnvioAbertaResolvida;
    }

    public Guid PagamentoPixId { get; }

    public StatusReconciliacaoPagamentoPix Status { get; }

    public Guid? OperacaoConsultaId { get; }

    public ResultadoOperacaoPagamentoPix? ResultadoOperacao { get; }

    public bool OperacaoEnvioAbertaResolvida { get; }

    public static ResultadoReconciliacaoPagamentoPix NaoAplicavel(Guid pagamentoPixId) =>
        new(pagamentoPixId, StatusReconciliacaoPagamentoPix.NaoAplicavel, null, null, false);

    public static ResultadoReconciliacaoPagamentoPix JaConclusivo(
        Guid pagamentoPixId,
        ResultadoOperacaoPagamentoPix resultadoOperacao) =>
        new(
            pagamentoPixId,
            StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo,
            null,
            resultadoOperacao,
            false);

    public static ResultadoReconciliacaoPagamentoPix Consultado(
        Guid pagamentoPixId,
        Guid operacaoConsultaId,
        ResultadoOperacaoPagamentoPix resultadoOperacao,
        bool operacaoEnvioAbertaResolvida) =>
        new(
            pagamentoPixId,
            StatusReconciliacaoPagamentoPix.Consultado,
            operacaoConsultaId,
            resultadoOperacao,
            operacaoEnvioAbertaResolvida);
}

using Domain.Enums;

namespace Application.Models;

public sealed record PreparacaoReconciliacaoPagamentoPixResult(
    StatusPreparacaoReconciliacaoPagamentoPix Status,
    Guid? OperacaoConsultaId,
    Guid? LeaseId,
    ResultadoOperacaoPagamentoPix? ResultadoOperacao,
    bool OperacaoEnvioAbertaResolvida)
{
    public static PreparacaoReconciliacaoPagamentoPixResult ConsultaPreparada(
        Guid operacaoConsultaId,
        Guid leaseId) =>
        new(StatusPreparacaoReconciliacaoPagamentoPix.ConsultaPreparada, operacaoConsultaId, leaseId, null, false);

    public static PreparacaoReconciliacaoPagamentoPixResult NaoAplicavel() =>
        new(StatusPreparacaoReconciliacaoPagamentoPix.NaoAplicavel, null, null, null, false);

    public static PreparacaoReconciliacaoPagamentoPixResult ConsultaEmAndamento() =>
        new(StatusPreparacaoReconciliacaoPagamentoPix.ConsultaEmAndamento, null, null, null, false);

    public static PreparacaoReconciliacaoPagamentoPixResult JaConclusivo(
        ResultadoOperacaoPagamentoPix resultadoOperacao,
        bool operacaoEnvioAbertaResolvida) =>
        new(
            StatusPreparacaoReconciliacaoPagamentoPix.ResultadoJaConclusivo,
            null,
            null,
            resultadoOperacao,
            operacaoEnvioAbertaResolvida);
}

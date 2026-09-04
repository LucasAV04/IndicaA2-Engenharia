using Domain.Enums;

namespace Application.Models;

public sealed record PreparacaoReconciliacaoPagamentoPixResult(
    StatusPreparacaoReconciliacaoPagamentoPix Status,
    Guid? OperacaoConsultaId,
    ResultadoOperacaoPagamentoPix? ResultadoOperacao,
    bool OperacaoEnvioAbertaResolvida)
{
    public static PreparacaoReconciliacaoPagamentoPixResult ConsultaPreparada(Guid operacaoConsultaId) =>
        new(StatusPreparacaoReconciliacaoPagamentoPix.ConsultaPreparada, operacaoConsultaId, null, false);

    public static PreparacaoReconciliacaoPagamentoPixResult NaoAplicavel() =>
        new(StatusPreparacaoReconciliacaoPagamentoPix.NaoAplicavel, null, null, false);

    public static PreparacaoReconciliacaoPagamentoPixResult ConsultaEmAndamento() =>
        new(StatusPreparacaoReconciliacaoPagamentoPix.ConsultaEmAndamento, null, null, false);

    public static PreparacaoReconciliacaoPagamentoPixResult JaConclusivo(
        ResultadoOperacaoPagamentoPix resultadoOperacao,
        bool operacaoEnvioAbertaResolvida) =>
        new(
            StatusPreparacaoReconciliacaoPagamentoPix.ResultadoJaConclusivo,
            null,
            resultadoOperacao,
            operacaoEnvioAbertaResolvida);
}

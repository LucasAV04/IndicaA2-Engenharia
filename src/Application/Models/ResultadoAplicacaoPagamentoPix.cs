using Domain.Enums;

namespace Application.Models;

public sealed record ResultadoAplicacaoPagamentoPix(
    Guid PagamentoPixId,
    StatusAplicacaoPagamentoPix Status,
    ResultadoOperacaoPagamentoPix? ResultadoOperacao)
{
    public static ResultadoAplicacaoPagamentoPix Aplicado(
        Guid pagamentoPixId,
        ResultadoOperacaoPagamentoPix resultadoOperacao) =>
        new(pagamentoPixId, StatusAplicacaoPagamentoPix.Aplicado, resultadoOperacao);

    public static ResultadoAplicacaoPagamentoPix JaAplicado(
        Guid pagamentoPixId,
        ResultadoOperacaoPagamentoPix resultadoOperacao) =>
        new(pagamentoPixId, StatusAplicacaoPagamentoPix.JaAplicado, resultadoOperacao);

    public static ResultadoAplicacaoPagamentoPix SemResultadoConclusivo(Guid pagamentoPixId) =>
        new(pagamentoPixId, StatusAplicacaoPagamentoPix.SemResultadoConclusivo, null);

    public static ResultadoAplicacaoPagamentoPix RequerReconciliacao(Guid pagamentoPixId) =>
        new(pagamentoPixId, StatusAplicacaoPagamentoPix.RequerReconciliacao, null);
}

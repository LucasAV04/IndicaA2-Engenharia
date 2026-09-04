using Domain.Enums;

namespace Application.Models;

/// <summary>
/// Resultado da decisão financeira tomada sob coordenação persistente.
/// </summary>
public sealed record ResultadoPersistenciaAplicacaoPagamentoPix(
    StatusPersistenciaAplicacaoPagamentoPix Status,
    ResultadoOperacaoPagamentoPix? ResultadoOperacao)
{
    public static ResultadoPersistenciaAplicacaoPagamentoPix Aplicado(
        ResultadoOperacaoPagamentoPix resultadoOperacao) =>
        new(StatusPersistenciaAplicacaoPagamentoPix.Aplicado, resultadoOperacao);

    public static ResultadoPersistenciaAplicacaoPagamentoPix JaAplicado(
        ResultadoOperacaoPagamentoPix resultadoOperacao) =>
        new(StatusPersistenciaAplicacaoPagamentoPix.JaAplicado, resultadoOperacao);

    public static ResultadoPersistenciaAplicacaoPagamentoPix SemResultadoConclusivo() =>
        new(StatusPersistenciaAplicacaoPagamentoPix.SemResultadoConclusivo, null);

    public static ResultadoPersistenciaAplicacaoPagamentoPix RequerReconciliacao() =>
        new(StatusPersistenciaAplicacaoPagamentoPix.RequerReconciliacao, null);
}

using Application.Models;
using Domain.Enums;

namespace Application.Interfaces.Stores;

/// <summary>
/// Prepara uma consulta de reconciliação sob o mesmo bloqueio persistente usado
/// pela aplicação financeira do resultado.
/// </summary>
public interface IPagamentoPixReconciliacaoStore
{
    Task<PreparacaoReconciliacaoPagamentoPixResult> PrepararConsultaAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default);

    Task<FinalizacaoConsultaPagamentoPixResult> FinalizarConsultaAsync(
        Guid pagamentoPixId,
        Guid operacaoConsultaId,
        Guid leaseId,
        ResultadoOperacaoPagamentoPix resultado,
        string? identificadorProvider,
        string? codigo,
        CancellationToken cancellationToken = default);
}

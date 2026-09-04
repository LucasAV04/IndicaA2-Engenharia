using Application.Models;

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
}

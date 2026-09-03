using Application.Models;

namespace Application.Interfaces.Services;

public interface IPagamentoPixReconciliacaoService
{
    Task<ResultadoReconciliacaoPagamentoPix> ReconciliarAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default);
}

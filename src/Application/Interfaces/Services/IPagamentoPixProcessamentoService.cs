using Application.Models;

namespace Application.Interfaces.Services;

/// <summary>
/// Coordena uma única etapa segura do ciclo de processamento de um Pagamento Pix.
/// </summary>
public interface IPagamentoPixProcessamentoService
{
    Task<ResultadoProcessamentoPagamentoPix> ProcessarAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default);
}

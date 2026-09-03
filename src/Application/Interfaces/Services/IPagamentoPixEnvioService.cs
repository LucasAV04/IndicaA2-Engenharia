using Application.Models;

namespace Application.Interfaces.Services;

public interface IPagamentoPixEnvioService
{
    Task<ResultadoEnvioPagamentoPix> ProcessarEnvioAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default);
}

using Application.Models;

namespace Application.Interfaces.Stores;

/// <summary>
/// Fronteira transacional para adquirir uma tentativa de envio e registrar sua
/// auditoria antes de qualquer chamada externa.
/// </summary>
public interface IPagamentoPixEnvioStore
{
    Task<PreparacaoEnvioPagamentoPixResult> TentarPrepararEnvioAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default);
}

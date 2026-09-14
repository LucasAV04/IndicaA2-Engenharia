using Application.Models;
using Domain.Enums;

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

    Task<FinalizacaoEnvioPagamentoPixResult> FinalizarEnvioAsync(
        Guid pagamentoPixId,
        Guid operacaoEnvioId,
        Guid leaseId,
        ResultadoOperacaoPagamentoPix resultado,
        string? identificadorProvider,
        string? codigo,
        CancellationToken cancellationToken = default);
}

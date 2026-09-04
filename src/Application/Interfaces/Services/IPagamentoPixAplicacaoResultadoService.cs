using Application.Models;

namespace Application.Interfaces.Services;

/// <summary>
/// Aplica o resultado financeiro já auditado de uma ordem Pix sem consultar ou enviar ao provider.
/// </summary>
public interface IPagamentoPixAplicacaoResultadoService
{
    Task<ResultadoAplicacaoPagamentoPix> AplicarAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default);
}

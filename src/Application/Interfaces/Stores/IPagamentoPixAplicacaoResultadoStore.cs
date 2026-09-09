using Application.Models;

namespace Application.Interfaces.Stores;

/// <summary>
/// Fronteira transacional para persistir, de forma atômica, o resultado financeiro de um Pagamento Pix e seu Cashback.
/// </summary>
public interface IPagamentoPixAplicacaoResultadoStore
{
    Task<ResultadoPersistenciaAplicacaoPagamentoPix> AplicarAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default);
}

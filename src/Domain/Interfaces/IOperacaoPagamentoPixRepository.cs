using Domain.Entities;

namespace Domain.Interfaces;

public interface IOperacaoPagamentoPixRepository
{
    Task AdicionarAsync(OperacaoPagamentoPix operacao, CancellationToken cancellationToken = default);

    Task<bool> FinalizarAsync(OperacaoPagamentoPix operacao, CancellationToken cancellationToken = default);

    Task<OperacaoPagamentoPix?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<OperacaoPagamentoPix>> ObterPorPagamentoPixIdAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<OperacaoPagamentoPix>> ObterAbertasAsync(
        CancellationToken cancellationToken = default);
}

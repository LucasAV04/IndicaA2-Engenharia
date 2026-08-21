using Domain.Entities;

namespace Domain.Interfaces;

public interface IPagamentoVistoriaRepository
{
    Task<PagamentoVistoria?> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PagamentoVistoria?> ObterPorVistoriaIdAsync(
        Guid vistoriaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PagamentoVistoria>> ObterTodosAsync(
        CancellationToken cancellationToken = default);

    Task AdicionarAsync(
        PagamentoVistoria pagamentoVistoria,
        CancellationToken cancellationToken = default);

    Task AtualizarAsync(
        PagamentoVistoria pagamentoVistoria,
        CancellationToken cancellationToken = default);
}

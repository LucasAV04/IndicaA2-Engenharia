using Domain.Entities;

namespace Domain.Interfaces;

public interface ICashbackRepository
{
    Task<Cashback?> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Cashback?> ObterPorPagamentoVistoriaIdAsync(
        Guid pagamentoVistoriaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Cashback>> ObterPorUsuarioIndicadorIdAsync(
        Guid usuarioIndicadorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Cashback>> ObterTodosAsync(
        CancellationToken cancellationToken = default);

    Task AdicionarAsync(
        Cashback cashback,
        CancellationToken cancellationToken = default);

    Task AtualizarAsync(
        Cashback cashback,
        CancellationToken cancellationToken = default);
}

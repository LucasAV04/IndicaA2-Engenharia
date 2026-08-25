using Domain.Entities;

namespace Domain.Interfaces;

public interface IPagamentoPixRepository
{
    Task<PagamentoPix?> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PagamentoPix?> ObterPorCashbackIdAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PagamentoPix>> ObterPorUsuarioBeneficiarioIdAsync(
        Guid usuarioBeneficiarioId,
        CancellationToken cancellationToken = default);

    Task AdicionarAsync(
        PagamentoPix pagamentoPix,
        CancellationToken cancellationToken = default);

    Task AtualizarAsync(
        PagamentoPix pagamentoPix,
        CancellationToken cancellationToken = default);
}

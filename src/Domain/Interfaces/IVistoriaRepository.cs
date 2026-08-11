using Domain.Entities;

namespace Domain.Interfaces;

public interface IVistoriaRepository
{
    Task<Vistoria?> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Vistoria>> ObterTodasAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Vistoria>> ObterPorUsuarioIdAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default);

    Task AdicionarAsync(
        Vistoria vistoria,
        CancellationToken cancellationToken = default);

    Task AtualizarAsync(
        Vistoria vistoria,
        CancellationToken cancellationToken = default);
}

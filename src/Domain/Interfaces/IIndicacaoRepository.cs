using Domain.Entities;
using Domain.Enums;

namespace Domain.Interfaces
{
    public interface IIndicacaoRepository
    {
        #region Consultas

        Task<Indicacao?> ObterPorIdAsync(Guid id,CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<Indicacao>> ObterTodasAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<Indicacao>> ObterPorUsuarioIndicadorIdAsync(Guid usuarioIndicadorId,CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<Indicacao>> ObterPorStatusAsync(StatusIndicacao status,CancellationToken cancellationToken = default);

        #endregion

        #region Comandos

        Task AdicionarAsync(Indicacao indicacao,CancellationToken cancellationToken = default);

        Task AtualizarAsync(Indicacao indicacao,CancellationToken cancellationToken = default);

        #endregion
    }
}

using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IUsuarioRepository
    {
        #region Consultas

        Task<Usuario?> ObterPorIdAsync(Guid id,CancellationToken cancellationToken = default);

        Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken cancellationToken = default);

        Task<Usuario?> ObterPorCodigoIndicacaoAsync(string codigoIndicacao, CancellationToken cancellationToken = default);

        Task<bool> ExistePorEmailAsync(string email, Guid? ignorarUsuarioId = null, CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<Usuario>> ObterTodosAsync(CancellationToken cancellationToken = default);

        Task<bool> ExistePorIdAsync(Guid id);


        #endregion

        #region Comandos

        Task AdicionarAsync(Usuario usuario, CancellationToken cancellationToken = default);

        Task AtualizarAsync(Usuario usuario, CancellationToken cancellationToken = default);

        #endregion
    }
}

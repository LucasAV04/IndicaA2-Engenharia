using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IUsuarioRepository
    {
        #region Consultas

        Task<Usuario?> ObterPorIdAsync(Guid id);

        Task<Usuario?> ObterPorEmailAsync(string email);

        Task<Usuario?> ObterPorCodigoIndicacaoAsync(string codigo);

        Task<IReadOnlyCollection<Usuario>> ObterTodosAsync();

        Task<bool> ExistePorIdAsync(Guid id);

        Task<bool> ExistePorEmailAsync(string email);

        #endregion

        #region Comandos

        Task AdicionarAsync(Usuario usuario);

        Task AtualizarAsync(Usuario usuario);

        Task RemoverAsync(Usuario usuario);

        #endregion
    }
}

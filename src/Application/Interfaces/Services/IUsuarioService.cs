using Application.DTOs.Usuario;

namespace Application.Interfaces.Services
{
    public interface IUsuarioService
    {
        Task<UsuarioResponseDto> CriarAsync(CreateUsuarioDto dto, CancellationToken cancellationToken = default);
        Task<UsuarioResponseDto> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IReadOnlyCollection<UsuarioResponseDto>> ObterTodosAsync(CancellationToken cancellationToken = default);
        Task AtualizarAsync(UpdateUsuarioDto dto, CancellationToken cancellationToken = default);
        Task AlterarSenhaAsync(AlterarSenhaUsuarioDto dto, CancellationToken cancellationToken = default);
        Task RemoverAsync(Guid id, CancellationToken cancellationToken = default);
    }
}

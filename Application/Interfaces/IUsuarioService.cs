using Application.DTOs.Usuario;

namespace Application.Interfaces
{
    public interface IUsuarioService
    {
        Task<UsuarioResponseDto> CriarAsync(CreateUsuarioDto dto);

        Task<UsuarioResponseDto?> ObterPorIdAsync(Guid id);

        Task<IReadOnlyCollection<UsuarioResponseDto>> ObterTodosAsync();

        Task AtualizarAsync(UpdateUsuarioDto dto);

        Task AlterarSenhaAsync(AlterarSenhaUsuarioDto dto);

        Task RemoverAsync(Guid id);
    }
}

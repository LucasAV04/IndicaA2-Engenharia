using Application.DTOs.DadosPix;

namespace Application.Interfaces.Services;

public interface IDadosPixService
{
    Task<DadosPixResponseDto> CadastrarOuAtualizarAsync(
        Guid usuarioId,
        DadosPixDto dto,
        CancellationToken cancellationToken = default);

    Task<DadosPixResponseDto?> ObterPorUsuarioIdAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default);

    Task RemoverAsync(Guid usuarioId, CancellationToken cancellationToken = default);
}

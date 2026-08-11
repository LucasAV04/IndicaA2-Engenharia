using Application.DTOs.Vistoria;

namespace Application.Interfaces.Services;

public interface IVistoriaService
{
    Task<VistoriaResponseDto> CriarAsync(
        CreateVistoriaDto dto,
        CancellationToken cancellationToken = default);

    Task<VistoriaResponseDto> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<VistoriaResponseDto>> ObterTodasAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<VistoriaResponseDto>> ObterPorUsuarioIdAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default);

    Task MarcarRealizadaAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task ConcluirAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task CancelarAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

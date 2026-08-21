using Application.DTOs.PagamentoVistoria;

namespace Application.Interfaces.Services;

public interface IPagamentoVistoriaService
{
    Task<PagamentoVistoriaResponseDto> CriarAsync(
        CreatePagamentoVistoriaDto dto,
        CancellationToken cancellationToken = default);

    Task<PagamentoVistoriaResponseDto> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PagamentoVistoriaResponseDto> ObterPorVistoriaIdAsync(
        Guid vistoriaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PagamentoVistoriaResponseDto>> ObterTodosAsync(
        CancellationToken cancellationToken = default);

    Task ConfirmarAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task CancelarAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

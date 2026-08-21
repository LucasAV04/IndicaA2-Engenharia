using Application.DTOs.Cashback;

namespace Application.Interfaces.Services;

public interface ICashbackService
{
    Task<CashbackResponseDto> GerarPorPagamentoAsync(
        Guid pagamentoVistoriaId,
        CancellationToken cancellationToken = default);

    Task<CashbackResponseDto> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CashbackResponseDto> ObterPorPagamentoVistoriaIdAsync(
        Guid pagamentoVistoriaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CashbackResponseDto>> ObterPorUsuarioIndicadorIdAsync(
        Guid usuarioIndicadorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CashbackResponseDto>> ObterTodosAsync(
        CancellationToken cancellationToken = default);

    Task AprovarAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default);

    Task CancelarAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default);
}

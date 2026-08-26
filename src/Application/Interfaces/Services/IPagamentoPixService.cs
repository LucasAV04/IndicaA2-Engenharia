using Application.DTOs.PagamentoPix;

namespace Application.Interfaces.Services;

public interface IPagamentoPixService
{
    Task<PagamentoPixResponseDto> CriarPorCashbackAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default);

    Task<PagamentoPixResponseDto> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PagamentoPixResponseDto> ObterPorCashbackIdAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PagamentoPixResponseDto>> ObterPorUsuarioBeneficiarioIdAsync(
        Guid usuarioBeneficiarioId,
        CancellationToken cancellationToken = default);

    Task CancelarAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

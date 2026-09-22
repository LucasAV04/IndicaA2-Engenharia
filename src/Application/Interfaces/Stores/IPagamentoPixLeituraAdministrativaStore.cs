using Application.DTOs.PagamentoPix;

namespace Application.Interfaces.Stores;

/// <summary>Projeção administrativa sem chave Pix, material criptográfico ou leases.</summary>
public interface IPagamentoPixLeituraAdministrativaStore
{
    Task<IReadOnlyCollection<PagamentoPixResponseDto>> ObterTodosAsync(CancellationToken cancellationToken = default);
}

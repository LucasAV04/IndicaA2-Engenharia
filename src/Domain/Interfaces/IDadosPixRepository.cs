using Domain.Entities;

namespace Domain.Interfaces;

public interface IDadosPixRepository
{
    Task<DadosPix?> ObterPorUsuarioIdAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    Task AdicionarAsync(DadosPix dadosPix, CancellationToken cancellationToken = default);

    Task AtualizarAsync(DadosPix dadosPix, CancellationToken cancellationToken = default);

    Task RemoverAsync(DadosPix dadosPix, CancellationToken cancellationToken = default);
}

namespace Application.Interfaces.Stores;

/// <summary>
/// Seleciona somente identificadores candidatos ao processamento. Não adquire
/// leases, não cria auditoria e não materializa dados Pix.
/// </summary>
public interface IPagamentoPixCandidatoProcessamentoStore
{
    Task<IReadOnlyCollection<Guid>> ObterCandidatosAsync(
        int limite,
        CancellationToken cancellationToken = default);
}

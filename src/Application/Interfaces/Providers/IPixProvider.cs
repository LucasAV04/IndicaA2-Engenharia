namespace Application.Interfaces.Providers;

/// <summary>
/// Porta da Application para um provedor financeiro de Pix.
/// Implementações concretas pertencem futuramente à Infrastructure.
/// </summary>
public interface IPixProvider
{
    Task<PixProviderResult> EnviarAsync(
        PixEnvioRequest request,
        CancellationToken cancellationToken = default);

    Task<PixProviderResult> ConsultarAsync(
        PixConsultaRequest request,
        CancellationToken cancellationToken = default);
}

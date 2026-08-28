namespace Application.Interfaces.Providers;

/// <summary>
/// Resultado provider-agnostic. Pendente e Indeterminado exigem reconciliação antes de qualquer retentativa.
/// </summary>
public sealed class PixProviderResult
{
    private PixProviderResult(
        StatusPixProvider status,
        string? identificadorProvider,
        string? codigo)
    {
        Status = status;
        IdentificadorProvider = identificadorProvider;
        Codigo = codigo;
    }

    public StatusPixProvider Status { get; }

    public string? IdentificadorProvider { get; }

    public string? Codigo { get; }

    public bool EhConfirmado => Status == StatusPixProvider.Confirmado;

    public bool EhFalhaConfirmada => Status == StatusPixProvider.FalhaConfirmada;

    public bool RequerReconciliacao => Status is StatusPixProvider.Pendente or StatusPixProvider.Indeterminado;

    public static PixProviderResult Confirmado(
        string? identificadorProvider = null,
        string? codigo = null) =>
        new(StatusPixProvider.Confirmado, identificadorProvider, codigo);

    public static PixProviderResult FalhaConfirmada(
        string? identificadorProvider = null,
        string? codigo = null) =>
        new(StatusPixProvider.FalhaConfirmada, identificadorProvider, codigo);

    public static PixProviderResult Pendente(
        string? identificadorProvider = null,
        string? codigo = null) =>
        new(StatusPixProvider.Pendente, identificadorProvider, codigo);

    public static PixProviderResult Indeterminado(
        string? identificadorProvider = null,
        string? codigo = null) =>
        new(StatusPixProvider.Indeterminado, identificadorProvider, codigo);
}

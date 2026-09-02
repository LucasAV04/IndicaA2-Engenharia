namespace Infrastructure.Providers;

/// <summary>
/// Configuração externa do adapter Efí Pix. Esta primeira integração aceita
/// exclusivamente o ambiente Sandbox/Homologação da Efí.
/// </summary>
public sealed class EfiPixOptions
{
    public const string SectionName = "EfiPix";

    public string Environment { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string CertificatePath { get; init; } = string.Empty;
    public string? CertificatePassword { get; init; }
    public string ChavePixPagador { get; init; } = string.Empty;

    public Uri ObterBaseUri()
    {
        ValidarParaSandbox();
        return new Uri(BaseUrl.TrimEnd('/') + '/', UriKind.Absolute);
    }

    public void ValidarParaSandbox()
    {
        if (!EhSandbox(Environment))
        {
            throw new InvalidOperationException(
                "O adapter Efí Pix desta etapa aceita somente o ambiente Sandbox/Homologação.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri)
            || !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(baseUri.Host, "pix-h.api.efipay.com.br", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "EfiPix:BaseUrl deve apontar exclusivamente para a base HTTPS de sandbox da Efí.");
        }

        ExigirValor(ClientId, "ClientId");
        ExigirValor(ClientSecret, "ClientSecret");
        ExigirValor(CertificatePath, "CertificatePath");
        ExigirValor(ChavePixPagador, "ChavePixPagador");

        if (!string.Equals(Path.GetExtension(CertificatePath), ".p12", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetExtension(CertificatePath), ".pfx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "O adapter Efí Pix desta etapa aceita certificado cliente P12/PFX externo.");
        }
    }

    private static bool EhSandbox(string environment) =>
        string.Equals(environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Homologacao", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Homologação", StringComparison.OrdinalIgnoreCase);

    private static void ExigirValor(string value, string nome) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(value, $"EfiPix:{nome}");
}

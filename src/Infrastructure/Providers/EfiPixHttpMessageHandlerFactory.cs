using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Infrastructure.Providers;

internal static class EfiPixHttpMessageHandlerFactory
{
    internal const X509KeyStorageFlags CertificateKeyStorageFlags = X509KeyStorageFlags.DefaultKeySet;

    public static HttpMessageHandler Criar(EfiPixOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ValidarParaSandbox();

        try
        {
            if (!File.Exists(options.CertificatePath))
            {
                throw new InvalidOperationException(
                    "O certificado P12/PFX externo configurado para a Efí não foi encontrado.");
            }

            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                options.CertificatePath,
                options.CertificatePassword,
                CertificateKeyStorageFlags);

            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new InvalidOperationException(
                    "O certificado P12/PFX da Efí deve conter uma chave privada.");
            }

            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(certificate);
            return handler;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "Não foi possível carregar o certificado P12/PFX externo da Efí.",
                exception);
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Infrastructure.Tests.Diagnostics;

/// <summary>
/// Diagnóstico externo manual do mTLS Efí. Não usa EfiPixProvider e não envia Pix.
/// Executar apenas no processo que recebeu as variáveis de homologação.
/// </summary>
public sealed class EfiPixTlsDiagnosticTests
{
    private const string OAuthEndpoint = "https://pix-h.api.efipay.com.br/oauth/token";
    private readonly ITestOutputHelper _output;

    public EfiPixTlsDiagnosticTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task OAuthSandbox_DeveCompararEstrategiasDeCarregamentoDoP12()
    {
        var configuration = SandboxConfiguration.TryCreate();
        if (configuration is null)
        {
            _output.WriteLine("Diagnóstico TLS não executado: variáveis obrigatórias de sandbox ausentes no processo atual.");
            return;
        }

        var strategies = new[]
        {
            new CertificateLoadStrategy("A-adapter-EphemeralKeySet", X509KeyStorageFlags.EphemeralKeySet),
            new CertificateLoadStrategy("B-DefaultKeySet", X509KeyStorageFlags.DefaultKeySet),
            // Comparação diagnóstica específica do Windows, sem MachineKeySet ou PersistKeySet.
            new CertificateLoadStrategy("C-UserKeySet", X509KeyStorageFlags.UserKeySet)
        };

        foreach (var strategy in strategies)
        {
            var result = await ExecuteOAuthAsync(configuration, strategy);
            _output.WriteLine(result.ToSafeOutput());
        }
    }

    private static async Task<OAuthDiagnosticResult> ExecuteOAuthAsync(
        SandboxConfiguration configuration,
        CertificateLoadStrategy strategy)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                configuration.CertificatePath,
                configuration.CertificatePassword,
                strategy.Flags);
        }
        catch (Exception exception)
        {
            return OAuthDiagnosticResult.CertificateLoadException(strategy.Name, exception, configuration);
        }

        using (certificate)
        {
            if (!certificate.HasPrivateKey)
            {
                return OAuthDiagnosticResult.CertificateWithoutPrivateKey(strategy.Name);
            }

            try
            {
                using var handler = new HttpClientHandler
                {
                    ClientCertificateOptions = ClientCertificateOption.Manual
                };
                handler.ClientCertificates.Add(certificate);

                using var client = new HttpClient(handler);
                using var request = new HttpRequestMessage(HttpMethod.Post, OAuthEndpoint);
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    $"{configuration.ClientId}:{configuration.ClientSecret}"));

                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Content = new StringContent(
                    "{\"grant_type\":\"client_credentials\"}",
                    Encoding.UTF8,
                    "application/json");

                using var response = await client.SendAsync(request);
                return OAuthDiagnosticResult.HttpResponse(
                    strategy.Name,
                    certificate.HasPrivateKey,
                    response.StatusCode);
            }
            catch (Exception exception)
            {
                return OAuthDiagnosticResult.TransportException(
                    strategy.Name,
                    certificate.HasPrivateKey,
                    exception,
                    configuration);
            }
        }
    }

    private sealed record CertificateLoadStrategy(string Name, X509KeyStorageFlags Flags);

    private sealed class SandboxConfiguration
    {
        private SandboxConfiguration(
            string clientId,
            string clientSecret,
            string certificatePath,
            string? certificatePassword)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            CertificatePath = certificatePath;
            CertificatePassword = certificatePassword;
        }

        public string ClientId { get; }
        public string ClientSecret { get; }
        public string CertificatePath { get; }
        public string? CertificatePassword { get; }

        public static SandboxConfiguration? TryCreate()
        {
            var clientId = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CLIENT_SECRET");
            var certificatePath = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CERTIFICATE_PATH");
            var certificatePassword = Environment.GetEnvironmentVariable("INDICA2_EFI_SANDBOX_CERTIFICATE_PASSWORD");

            return string.IsNullOrWhiteSpace(clientId)
                || string.IsNullOrWhiteSpace(clientSecret)
                || string.IsNullOrWhiteSpace(certificatePath)
                ? null
                : new SandboxConfiguration(clientId, clientSecret, certificatePath, certificatePassword);
        }
    }

    private sealed class OAuthDiagnosticResult
    {
        private OAuthDiagnosticResult(
            string strategy,
            bool certificateLoaded,
            bool hasPrivateKey,
            bool reachedHttp,
            HttpStatusCode? statusCode,
            string? exceptionType,
            string? exceptionMessage,
            string? innerException)
        {
            Strategy = strategy;
            CertificateLoaded = certificateLoaded;
            HasPrivateKey = hasPrivateKey;
            ReachedHttp = reachedHttp;
            StatusCode = statusCode;
            ExceptionType = exceptionType;
            ExceptionMessage = exceptionMessage;
            InnerException = innerException;
        }

        public string Strategy { get; }
        public bool CertificateLoaded { get; }
        public bool HasPrivateKey { get; }
        public bool ReachedHttp { get; }
        public HttpStatusCode? StatusCode { get; }
        public string? ExceptionType { get; }
        public string? ExceptionMessage { get; }
        public string? InnerException { get; }

        public static OAuthDiagnosticResult CertificateWithoutPrivateKey(string strategy) =>
            new(strategy, true, false, false, null, null, null, null);

        public static OAuthDiagnosticResult HttpResponse(
            string strategy,
            bool hasPrivateKey,
            HttpStatusCode statusCode) =>
            new(strategy, true, hasPrivateKey, true, statusCode, null, null, null);

        public static OAuthDiagnosticResult CertificateLoadException(
            string strategy,
            Exception exception,
            SandboxConfiguration configuration) =>
            new(
                strategy,
                certificateLoaded: false,
                hasPrivateKey: false,
                reachedHttp: false,
                statusCode: null,
                exceptionType: exception.GetType().Name,
                exceptionMessage: Sanitize(exception.Message, configuration),
                innerException: exception.InnerException is null
                    ? null
                    : $"{exception.InnerException.GetType().Name}: {Sanitize(exception.InnerException.Message, configuration)}");

        public static OAuthDiagnosticResult TransportException(
            string strategy,
            bool hasPrivateKey,
            Exception exception,
            SandboxConfiguration configuration) =>
            new(
                strategy,
                certificateLoaded: true,
                hasPrivateKey,
                reachedHttp: false,
                statusCode: null,
                exceptionType: exception.GetType().Name,
                exceptionMessage: Sanitize(exception.Message, configuration),
                innerException: exception.InnerException is null
                    ? null
                    : $"{exception.InnerException.GetType().Name}: {Sanitize(exception.InnerException.Message, configuration)}");

        public string ToSafeOutput() =>
            $"Estratégia={Strategy}; certificado carregou={CertificateLoaded}; " +
            $"HasPrivateKey={HasPrivateKey}; handshake chegou a HTTP={ReachedHttp}; " +
            $"código HTTP={(StatusCode is null ? "nenhum" : ((int)StatusCode).ToString())}; " +
            $"OAuth retornou 200={StatusCode == HttpStatusCode.OK}; " +
            $"exceção={(ExceptionType is null ? "nenhuma" : $"{ExceptionType}: {ExceptionMessage}")}; " +
            $"inner={(InnerException ?? "nenhuma")}.";

        private static string Sanitize(string? message, SandboxConfiguration configuration)
        {
            var sanitized = message ?? string.Empty;
            foreach (var sensitive in new[]
                     {
                         configuration.ClientId,
                         configuration.ClientSecret,
                         configuration.CertificatePath,
                         configuration.CertificatePassword
                     }.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                sanitized = sanitized.Replace(sensitive!, "[redigido]", StringComparison.Ordinal);
            }

            sanitized = Regex.Replace(sanitized, @"(?i)\b(bearer|basic)\s+[^\s]+", "$1 [redigido]");
            return sanitized.Length <= 500 ? sanitized : sanitized[..500] + "…";
        }
    }
}

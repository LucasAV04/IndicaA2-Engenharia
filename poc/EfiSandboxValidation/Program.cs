using Efipay;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

var clientId = GetRequiredEnvironmentVariable("EFI_CLIENT_ID");
var clientSecret = GetRequiredEnvironmentVariable("EFI_CLIENT_SECRET");
var certificatePath = GetRequiredEnvironmentVariable("EFI_CERTIFICATE_PATH");
_ = GetRequiredEnvironmentVariable("EFI_PIX_KEY");

if (!bool.TryParse(GetRequiredEnvironmentVariable("EFI_SANDBOX"), out var sandbox) || !sandbox)
{
    throw new InvalidOperationException("A POC aceita exclusivamente EFI_SANDBOX=true.");
}

if (!File.Exists(certificatePath))
{
    throw new InvalidOperationException("O certificado configurado não foi encontrado localmente.");
}

var certificateExtension = Path.GetExtension(certificatePath);

if (!string.Equals(certificateExtension, ".p12", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(certificateExtension, ".pfx", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("A POC aceita certificado .p12 ou .pfx.");
}

dynamic efi = new EfiPay(clientId, clientSecret, sandbox: true, certificate: certificatePath);
var tomorrow = DateTime.UtcNow.Date.AddDays(1);
var parameters = new
{
    inicio = tomorrow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
    fim = tomorrow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
};

var sdkSucceeded = false;

try
{
    // Consulta sem movimentação financeira. A SDK realiza OAuth e mTLS antes do GET.
    _ = efi.PixSendList(parameters, null);
    Console.WriteLine("SAFE_QUERY=SUCCESS");
    Console.WriteLine("SDK_ROUTE=GET /v2/gn/pix/enviados");
    sdkSucceeded = true;
}
catch (EfiException exception)
{
    Console.Error.WriteLine("SAFE_QUERY=FAILURE");
    Console.Error.WriteLine($"EFI_ERROR_TYPE={exception.ErrorType}");
    Console.Error.WriteLine($"EFI_ERROR_CODE={exception.Code}");
}

if (!sdkSucceeded)
{
    var restAuthenticationSucceeded = await RunRestAuthenticationDiagnosticAsync(
        clientId,
        clientSecret,
        certificatePath);

    Environment.ExitCode = restAuthenticationSucceeded ? 2 : 1;
}

static string GetRequiredEnvironmentVariable(string name)
{
    var value = Environment.GetEnvironmentVariable(name);

    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"A variável {name} não foi configurada.");
}

static async Task<bool> RunRestAuthenticationDiagnosticAsync(
    string clientId,
    string clientSecret,
    string certificatePath)
{
    using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
        certificatePath,
        string.Empty,
        X509KeyStorageFlags.EphemeralKeySet);
    using var handler = new HttpClientHandler();
    handler.ClientCertificates.Add(certificate);
    using var client = new HttpClient(handler);
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        "https://pix-h.api.efipay.com.br/oauth/token");

    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    request.Content = new StringContent("{\"grant_type\":\"client_credentials\"}", Encoding.UTF8, "application/json");

    HttpResponseMessage response;

    try
    {
        response = await client.SendAsync(request);
    }
    catch (HttpRequestException)
    {
        Console.WriteLine("REST_AUTH_TRANSPORT=FAILURE");
        Console.WriteLine("REST_AUTH_FAILURE_STAGE=TLS_MTLS");
        return false;
    }

    using (response)
    {
        Console.WriteLine($"REST_AUTH_STATUS={(int)response.StatusCode}");

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine("REST_AUTH=SUCCESS");
            return true;
        }

        var content = await response.Content.ReadAsStringAsync();

        try
        {
            using var document = JsonDocument.Parse(content);
            var propertyNames = document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal);
            Console.WriteLine($"REST_AUTH_ERROR_PROPERTIES={string.Join(',', propertyNames)}");
        }
        catch (JsonException)
        {
            Console.WriteLine("REST_AUTH_ERROR_PROPERTIES=unavailable");
        }

        return false;
    }
}

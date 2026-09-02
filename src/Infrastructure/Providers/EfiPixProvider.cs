using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Interfaces.Providers;

namespace Infrastructure.Providers;

/// <summary>
/// Adapter HTTP da Efí para Sandbox/Homologação. A confirmação financeira
/// assíncrona por webhook permanece deliberadamente fora deste adapter.
/// </summary>
public sealed class EfiPixProvider : IPixProvider
{
    public const string HttpClientName = "EfiPixSandbox";

    private const string EscopoEnvio = "pix.send";
    private const string EscopoConsulta = "gn.pix.send.read";
    private readonly HttpClient _httpClient;
    private readonly EfiPixOptions _options;
    private readonly EfiPixAccessTokenCache _accessTokenCache;
    private readonly Uri _baseUri;

    public EfiPixProvider(HttpClient httpClient, EfiPixOptions options)
        : this(httpClient, options, new EfiPixAccessTokenCache())
    {
    }

    internal EfiPixProvider(
        HttpClient httpClient,
        EfiPixOptions options,
        EfiPixAccessTokenCache accessTokenCache)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(accessTokenCache);

        _httpClient = httpClient;
        _options = options;
        _accessTokenCache = accessTokenCache;
        _baseUri = options.ObterBaseUri();
    }

    public async Task<PixProviderResult> EnviarAsync(
        PixEnvioRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _options.ValidarParaEnvio();

        try
        {
            var accessToken = await ObterAccessTokenAsync(EscopoEnvio, cancellationToken);
            using var message = new HttpRequestMessage(
                HttpMethod.Put,
                new Uri(_baseUri, $"v3/gn/pix/{request.ReferenciaIdempotente}"));

            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            message.Content = JsonContent.Create(new EfiPixEnvioPayload(
                request.Valor.ToString("F2", CultureInfo.InvariantCulture),
                new EfiPixChave(_options.ChavePixPagador),
                new EfiPixChave(request.ChavePix)));

            using var response = await _httpClient.SendAsync(message, cancellationToken);
            return await TraduzirRespostaEnvioAsync(response, request.ReferenciaIdempotente, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return PixProviderResult.Indeterminado(codigo: "timeout");
        }
        catch (EfiPixAuthenticationException)
        {
            return PixProviderResult.Indeterminado(codigo: "oauth");
        }
        catch (HttpRequestException)
        {
            return PixProviderResult.Indeterminado(codigo: "transport");
        }
        catch (JsonException)
        {
            return PixProviderResult.Indeterminado(codigo: "invalid-response");
        }
    }

    public async Task<PixProviderResult> ConsultarAsync(
        PixConsultaRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var accessToken = await ObterAccessTokenAsync(EscopoConsulta, cancellationToken);
            using var message = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(_baseUri, $"v2/gn/pix/enviados/id-envio/{request.ReferenciaIdempotente}"));

            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(message, cancellationToken);
            return await TraduzirRespostaConsultaAsync(response, request.ReferenciaIdempotente, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return PixProviderResult.Indeterminado(codigo: "timeout");
        }
        catch (EfiPixAuthenticationException)
        {
            return PixProviderResult.Indeterminado(codigo: "oauth");
        }
        catch (HttpRequestException)
        {
            return PixProviderResult.Indeterminado(codigo: "transport");
        }
        catch (JsonException)
        {
            return PixProviderResult.Indeterminado(codigo: "invalid-response");
        }
    }

    private async Task<string> ObterAccessTokenAsync(string scope, CancellationToken cancellationToken) =>
        await _accessTokenCache.ObterAsync(
            scope,
            tokenCancellationToken => SolicitarAccessTokenAsync(scope, tokenCancellationToken),
            cancellationToken);

    private async Task<EfiPixAccessToken> SolicitarAccessTokenAsync(
        string scope,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "oauth/token"));
        var basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{_options.ClientId}:{_options.ClientSecret}"));

        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCredentials);
        message.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = scope
        });

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new EfiPixAuthenticationException();

        var token = await response.Content.ReadFromJsonAsync<EfiPixOAuthResponse>(cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.AccessToken) || token.ExpiresIn <= 0)
            throw new EfiPixAuthenticationException();

        return new EfiPixAccessToken(token.AccessToken, token.ExpiresIn);
    }

    private static async Task<PixProviderResult> TraduzirRespostaEnvioAsync(
        HttpResponseMessage response,
        string referenciaIdempotente,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            return PixProviderResult.FalhaConfirmada(codigo: $"http-{(int)response.StatusCode}");

        if (response.StatusCode != HttpStatusCode.Created)
            return PixProviderResult.Indeterminado(codigo: $"http-{(int)response.StatusCode}");

        var body = await response.Content.ReadFromJsonAsync<EfiPixOperationResponse>(cancellationToken)
            ?? throw new JsonException("A resposta da Efí não contém JSON válido.");

        return TraduzirStatus(body.Status, body.IdEnvio ?? referenciaIdempotente);
    }

    private static async Task<PixProviderResult> TraduzirRespostaConsultaAsync(
        HttpResponseMessage response,
        string referenciaIdempotente,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            return PixProviderResult.FalhaConfirmada(codigo: "http-404");

        if (response.StatusCode != HttpStatusCode.OK)
            return PixProviderResult.Indeterminado(codigo: $"http-{(int)response.StatusCode}");

        var body = await response.Content.ReadFromJsonAsync<EfiPixOperationResponse>(cancellationToken)
            ?? throw new JsonException("A resposta da Efí não contém JSON válido.");

        return TraduzirStatus(body.Status, body.IdEnvio ?? referenciaIdempotente);
    }

    private static PixProviderResult TraduzirStatus(string? status, string identificadorProvider) =>
        status?.Trim().ToUpperInvariant() switch
        {
            "REALIZADO" => PixProviderResult.Confirmado(identificadorProvider, "REALIZADO"),
            "REJEITADO" => PixProviderResult.FalhaConfirmada(identificadorProvider, "REJEITADO"),
            "EM_PROCESSAMENTO" => PixProviderResult.Pendente(identificadorProvider, "EM_PROCESSAMENTO"),
            _ => PixProviderResult.Indeterminado(identificadorProvider, "unknown-status")
        };

    private sealed class EfiPixAuthenticationException : Exception;

    private sealed record EfiPixEnvioPayload(
        [property: JsonPropertyName("valor")] string Valor,
        [property: JsonPropertyName("pagador")] EfiPixChave Pagador,
        [property: JsonPropertyName("favorecido")] EfiPixChave Favorecido);

    private sealed record EfiPixChave([property: JsonPropertyName("chave")] string Chave);

    private sealed class EfiPixOAuthResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }

    private sealed class EfiPixOperationResponse
    {
        [JsonPropertyName("idEnvio")]
        public string? IdEnvio { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }
}

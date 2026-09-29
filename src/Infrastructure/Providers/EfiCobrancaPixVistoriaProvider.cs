using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Application.Recebimentos;

namespace Infrastructure.Providers;

public sealed class EfiCobrancaPixVistoriaProvider : ICobrancaPixVistoriaProvider
{
    public const string HttpClientName = "EfiRecebimentoSandbox";
    private readonly HttpClient _http;
    private readonly EfiPixOptions _efi;
    private readonly RecebimentoPixOptions _options;
    private readonly EfiPixAccessTokenCache _cache;
    private readonly TimeProvider _clock;
    private readonly Uri _base;
    public EfiCobrancaPixVistoriaProvider(HttpClient http, EfiPixOptions efi, RecebimentoPixOptions options, TimeProvider clock)
        : this(http, efi, options, clock, new EfiPixAccessTokenCache(clock)) { }
    internal EfiCobrancaPixVistoriaProvider(HttpClient http, EfiPixOptions efi, RecebimentoPixOptions options,
        TimeProvider clock, EfiPixAccessTokenCache cache)
    {
        _http = http; _efi = efi; _options = options; _clock = clock; _cache = cache; _base = efi.ObterBaseUri();
        if (_base.Port != 443 || _base.AbsolutePath != "/" || _base.UserInfo.Length != 0
            || _base.Query.Length != 0 || _base.Fragment.Length != 0)
            throw new InvalidOperationException("Base HTTPS de recebimento inválida.");
    }
    public Task<ResultadoCobrancaProvider> CriarAsync(string txid, decimal valor, int expiracaoSegundos, CancellationToken ct)
    {
        if (valor <= 0 || decimal.Round(valor, 2) != valor || expiracaoSegundos is < 60 or > 86400)
            throw new ArgumentException("Parâmetros da cobrança inválidos.");
        return CobrancaAsync(HttpMethod.Put, txid, "cob.write", new
        {
            calendario = new { expiracao = expiracaoSegundos },
            valor = new { original = valor.ToString("F2", CultureInfo.InvariantCulture) },
            chave = _options.ChaveRecebedora,
            solicitacaoPagador = "Pagamento de vistoria A2"
        }, ct);
    }
    public Task<ResultadoCobrancaProvider> ConsultarAsync(string txid, CancellationToken ct) =>
        CobrancaAsync(HttpMethod.Get, txid, "cob.read", null, ct);
    public Task<ResultadoCobrancaProvider> RemoverAsync(string txid, CancellationToken ct) =>
        CobrancaAsync(HttpMethod.Patch, txid, "cob.write", new { status = "REMOVIDA_PELO_USUARIO_RECEBEDOR" }, ct);

    private async Task<ResultadoCobrancaProvider> CobrancaAsync(HttpMethod method, string txid, string scope, object? payload, CancellationToken ct)
    {
        if (!RecebimentoPixValidacao.TxidValido(txid)) throw new ArgumentException("txid inválido.");
        try
        {
            using var response = await EnviarAsync(method, "v2/cob/" + txid, scope, payload, ct);
            if (response.StatusCode == HttpStatusCode.NotFound && method == HttpMethod.Get)
                return new(SituacaoCobrancaProvider.Ausente, "not-found");
            if (!response.IsSuccessStatusCode) return new(SituacaoCobrancaProvider.Indeterminada, "http-" + (int)response.StatusCode);
            using var json = await LerJsonAsync(response.Content, ct);
            var root = json.RootElement;
            var state = Texto(root, "status") switch
            {
                "ATIVA" => SituacaoCobrancaProvider.Ativa,
                "CONCLUIDA" => SituacaoCobrancaProvider.Concluida,
                "REMOVIDA_PELO_USUARIO_RECEBEDOR" or "REMOVIDA_PELO_PSP" => SituacaoCobrancaProvider.Removida,
                _ => SituacaoCobrancaProvider.Indeterminada
            };
            if (state == SituacaoCobrancaProvider.Indeterminada) return new(state, "unknown-status");
            var receivedTxid = Texto(root, "txid");
            var valor = Decimal(root.GetProperty("valor").GetProperty("original"));
            var calendario = root.GetProperty("calendario");
            var criada = Data(calendario.GetProperty("criacao"));
            var prazo = int.Parse(calendario.GetProperty("expiracao").ToString(), CultureInfo.InvariantCulture);
            var revision = root.GetProperty("revisao").GetInt32();
            var codigo = root.TryGetProperty("pixCopiaECola", out var pix) ? pix.GetString() : null;
            if ((codigo is not null && Encoding.UTF8.GetByteCount(codigo) > 8192) || receivedTxid != txid || prazo is < 1 or > 86400 || revision < 0)
                return new(SituacaoCobrancaProvider.Indeterminada, "invalid-response");
            var eventos = new List<EventoPix>();
            if (root.TryGetProperty("pix", out var recebimentos))
            {
                if (recebimentos.GetArrayLength() > 100) throw new JsonException();
                foreach (var item in recebimentos.EnumerateArray()) eventos.Add(Evento(item));
            }
            return new(state, state.ToString(), receivedTxid, valor, criada, prazo, revision, codigo, eventos);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { return new(SituacaoCobrancaProvider.Indeterminada, "unavailable"); }
    }
    public async Task<EventoPix?> ConsultarRecebimentoAsync(string endToEndId, CancellationToken ct)
    {
        if (!RecebimentoPixValidacao.E2eValido(endToEndId)) throw new ArgumentException("Identificador de recebimento inválido.");
        try
        {
            using var response = await EnviarAsync(HttpMethod.Get, "v2/pix/" + endToEndId, "pix.read", null, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var json = await LerJsonAsync(response.Content, ct);
            return Evento(json.RootElement);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException) { return null; }
    }
    public async Task<bool> ConfigurarWebhookAsync(CancellationToken ct)
    {
        using var response = await EnviarAsync(HttpMethod.Put, "v2/webhook/" + Uri.EscapeDataString(_options.ChaveRecebedora), "webhook.write", new { webhookUrl = _options.WebhookUrl }, ct);
        return response.IsSuccessStatusCode;
    }
    public async Task<bool> ConsultarWebhookAsync(CancellationToken ct)
    {
        using var response = await EnviarAsync(HttpMethod.Get, "v2/webhook/" + Uri.EscapeDataString(_options.ChaveRecebedora), "webhook.read", null, ct);
        if (!response.IsSuccessStatusCode) return false;
        using var json = await LerJsonAsync(response.Content, ct);
        return Texto(json.RootElement, "webhookUrl") == _options.WebhookUrl;
    }
    private EventoPix Evento(JsonElement element)
    {
        var e = new EventoPix(Texto(element, "endToEndId"), Texto(element, "txid"), Decimal(element.GetProperty("valor")), Data(element.GetProperty("horario")));
        if (!RecebimentoPixValidacao.EventoValido(e, _clock.GetUtcNow().UtcDateTime)) throw new JsonException();
        return e;
    }
    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod method, string path, string scope, object? payload, CancellationToken ct)
    {
        _options.ExigirHabilitado();
        var token = await _cache.ObterAsync(scope, async cancellation =>
        {
            using var oauth = new HttpRequestMessage(HttpMethod.Post, new Uri(_base, "oauth/token"));
            oauth.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_efi.ClientId + ":" + _efi.ClientSecret)));
            oauth.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = scope });
            using var r = await _http.SendAsync(oauth, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!r.IsSuccessStatusCode) throw new HttpRequestException("OAuth indisponível.");
            using var json = await LerJsonAsync(r.Content, cancellation);
            var value = Texto(json.RootElement, "access_token");
            var seconds = json.RootElement.GetProperty("expires_in").GetInt32();
            if (value.Length is < 1 or > 16384 || seconds is < 1 or > 86400) throw new JsonException();
            return new EfiPixAccessToken(value, seconds);
        }, ct);
        using var request = new HttpRequestMessage(method, new Uri(_base, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    internal static async Task<JsonDocument> LerJsonAsync(HttpContent content, CancellationToken ct)
    {
        // ResponseHeadersRead encerra o timeout do HttpClient nos headers.
        // O corpo também precisa de limite próprio, inferior ao lease persistente.
        using var leitura = CancellationTokenSource.CreateLinkedTokenSource(ct);
        leitura.CancelAfter(TimeSpan.FromSeconds(30));
        ct = leitura.Token;
        const int limit = 65536;
        if (content.Headers.ContentLength > limit) throw new JsonException();
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > limit) throw new JsonException();
            buffer.Write(chunk, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
    }
    private static string Texto(JsonElement root, string name) => root.GetProperty(name).GetString() ?? throw new JsonException();
    private static decimal Decimal(JsonElement e) => decimal.Parse(e.GetString() ?? throw new JsonException(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private static DateTime Data(JsonElement e)
    {
        var value = e.GetString() ?? throw new JsonException();
        if (!value.EndsWith('Z') && !System.Text.RegularExpressions.Regex.IsMatch(value, @"[+-]\d{2}:\d{2}$")) throw new JsonException();
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).UtcDateTime;
    }
}

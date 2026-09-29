using System.Net;
using System.Text;
using System.Text.Json;
using Application.Recebimentos;
using Infrastructure.Providers;
using Xunit;

namespace Infrastructure.Tests.Providers;

public sealed class EfiCobrancaPixVistoriaProviderTests
{
    private const string Txid = "11111111111111111111111111111111";
    private const string E2e = "E00000000202609251200ABCDEFGHIJK";
    private const string Chave = "recebedor-ficticio@example.invalid";

    [Fact]
    public async Task CriarUsaPutIdentidadePersistidaValorInvariantESomenteCamposNecessarios()
    {
        using var handler = new FakeHttp(Json(Cobranca()));
        using var http = new HttpClient(handler);
        var result = await Provider(http).CriarAsync(Txid, 12.34m, 3600, default);
        Assert.Equal(SituacaoCobrancaProvider.Ativa, result.Situacao);
        Assert.Equal(Txid, result.Txid);
        Assert.Equal(12.34m, result.Valor);
        Assert.Equal("cob.write", handler.Scopes.Single());
        var request = Assert.Single(handler.Operacoes);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/v2/cob/" + Txid, request.Path);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("12.34", body.RootElement.GetProperty("valor").GetProperty("original").GetString());
        Assert.Equal(3600, body.RootElement.GetProperty("calendario").GetProperty("expiracao").GetInt32());
        Assert.Equal(Chave, body.RootElement.GetProperty("chave").GetString());
        Assert.Equal(4, body.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("codigo-ficticio", result.ToString());
    }

    [Theory]
    [InlineData(false, "GET", "cob.read")]
    [InlineData(true, "PATCH", "cob.write")]
    public async Task ConsultaERemocaoUsamMesmaIdentidadeSemNovoPut(bool remover, string metodo, string scope)
    {
        using var handler = new FakeHttp(Json(Cobranca(remover ? "REMOVIDA_PELO_USUARIO_RECEBEDOR" : "ATIVA")));
        using var http = new HttpClient(handler);
        var provider = Provider(http);
        var result = remover ? await provider.RemoverAsync(Txid, default) : await provider.ConsultarAsync(Txid, default);
        Assert.Equal(remover ? SituacaoCobrancaProvider.Removida : SituacaoCobrancaProvider.Ativa, result.Situacao);
        var request = Assert.Single(handler.Operacoes);
        Assert.Equal(metodo, request.Method.Method);
        Assert.Equal("/v2/cob/" + Txid, request.Path);
        Assert.Equal(scope, handler.Scopes.Single());
        if (remover) Assert.Equal("{\"status\":\"REMOVIDA_PELO_USUARIO_RECEBEDOR\"}", request.Body);
        else Assert.Null(request.Body);
    }

    [Fact]
    public async Task RecebimentoConsultaE2eComEscopoPixReadSemPropagarDadosPessoais()
    {
        var evento = JsonSerializer.Serialize(new { endToEndId = E2e, txid = Txid, valor = "12.34", horario = "2026-09-25T12:00:00Z", pagador = new { nome = "SEGREDO_FICTICIO" } });
        using var handler = new FakeHttp(Json(evento)); using var http = new HttpClient(handler);
        var result = await Provider(http).ConsultarRecebimentoAsync(E2e, default);
        Assert.NotNull(result); Assert.Equal(E2e, result.EndToEndId);
        Assert.Equal("pix.read", handler.Scopes.Single());
        Assert.Equal("/v2/pix/" + E2e, Assert.Single(handler.Operacoes).Path);
        Assert.DoesNotContain("SEGREDO_FICTICIO", result.ToString());
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(409)] [InlineData(422)] [InlineData(429)] [InlineData(500)] [InlineData(503)]
    public async Task ErroHttpNuncaViraConfirmacaoNemRetry(int status)
    {
        using var handler = new FakeHttp(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("SEGREDO_FICTICIO") });
        using var http = new HttpClient(handler);
        var result = await Provider(http).CriarAsync(Txid, 12.34m, 3600, default);
        Assert.Equal(SituacaoCobrancaProvider.Indeterminada, result.Situacao);
        Assert.Single(handler.Operacoes); Assert.DoesNotContain("SEGREDO_FICTICIO", result.ToString());
    }

    [Fact]
    public async Task ApenasGet404ComprovaAusencia()
    {
        using var handler = new FakeHttp(new HttpResponseMessage(HttpStatusCode.NotFound)); using var http = new HttpClient(handler);
        Assert.Equal(SituacaoCobrancaProvider.Ausente, (await Provider(http).ConsultarAsync(Txid, default)).Situacao);
        Assert.Single(handler.Operacoes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":\"NOVO_STATUS\"}")]
    [InlineData("nao-json SEGREDO_FICTICIO")]
    public async Task RespostaIncompletaOuDesconhecidaFalhaFechado(string body)
    {
        using var handler = new FakeHttp(Json(body)); using var http = new HttpClient(handler);
        var result = await Provider(http).ConsultarAsync(Txid, default);
        Assert.Equal(SituacaoCobrancaProvider.Indeterminada, result.Situacao);
        Assert.DoesNotContain("SEGREDO_FICTICIO", result.ToString());
    }

    [Fact]
    public async Task CorpoMaiorQueLimiteFalhaFechado()
    {
        using var handler = new FakeHttp(Json(new string('x', 65537))); using var http = new HttpClient(handler);
        Assert.Equal(SituacaoCobrancaProvider.Indeterminada, (await Provider(http).ConsultarAsync(Txid, default)).Situacao);
    }

    [Fact]
    public async Task CancelamentoNaoEhConvertidoEmRespostaFinanceira()
    {
        using var handler = new FakeHttp(Json(Cobranca())); using var http = new HttpClient(handler);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(http).ConsultarAsync(Txid, cts.Token));
        Assert.Empty(handler.Operacoes);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task WebhookUsaSomenteConfiguracaoExternaEEscopoEspecifico(bool configurar)
    {
        using var handler = new FakeHttp(Json("{\"webhookUrl\":\"https://example.invalid/api/webhooks/efi\"}")); using var http = new HttpClient(handler);
        var provider = Provider(http);
        Assert.True(configurar ? await provider.ConfigurarWebhookAsync(default) : await provider.ConsultarWebhookAsync(default));
        Assert.Equal(configurar ? "webhook.write" : "webhook.read", handler.Scopes.Single());
        var request = Assert.Single(handler.Operacoes);
        Assert.Equal("/v2/webhook/" + Uri.EscapeDataString(Chave), request.Path);
        Assert.Equal(configurar ? HttpMethod.Put : HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task ConsultaConcluidaMantemEventoParaVerificacaoSeparada()
    {
        var body=JsonSerializer.Serialize(new { status="CONCLUIDA", txid=Txid, valor=new { original="12.34" },
            calendario=new { criacao="2026-09-25T11:00:00Z", expiracao=3600 }, revisao=1,
            pix=new[]{new { endToEndId=E2e, txid=Txid, valor="12.34", horario="2026-09-25T11:59:00Z" }} });
        using var handler=new FakeHttp(Json(body)); using var http=new HttpClient(handler);
        var result=await Provider(http).ConsultarAsync(Txid,default);
        Assert.Equal(SituacaoCobrancaProvider.Concluida,result.Situacao);
        Assert.Equal(E2e,Assert.Single(result.Recebimentos!).EndToEndId);
        Assert.Single(handler.Operacoes);
    }

    [Fact]
    public async Task TimeoutDeTransporteNaoRepeteOperacaoNemExpoeMensagem()
    {
        using var handler=new TransporteFalho(); using var http=new HttpClient(handler);
        var result=await Provider(http).ConsultarAsync(Txid,default);
        Assert.Equal(SituacaoCobrancaProvider.Indeterminada,result.Situacao);
        Assert.DoesNotContain("SEGREDO_FICTICIO",result.ToString());
        Assert.Equal(1,handler.Chamadas);
    }

    private sealed class TransporteFalho : HttpMessageHandler
    {
        public int Chamadas { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Chamadas++; throw new TaskCanceledException("SEGREDO_FICTICIO"); }
    }

    private static EfiCobrancaPixVistoriaProvider Provider(HttpClient http) => new(http,
        new EfiPixOptions { Environment = "Sandbox", BaseUrl = "https://pix-h.api.efipay.com.br", ClientId = "id-ficticio", ClientSecret = "segredo-ficticio", CertificatePath = "nao-carregado.p12" },
        new RecebimentoPixOptions { Habilitado = true, ChaveRecebedora = Chave, WebhookUrl = "https://example.invalid/api/webhooks/efi" }, new Clock());
    private static string Cobranca(string status = "ATIVA") => JsonSerializer.Serialize(new
    {
        status, txid = Txid, valor = new { original = "12.34" }, calendario = new { criacao = "2026-09-25T12:00:00Z", expiracao = 3600 }, revisao = 0, pixCopiaECola = "codigo-ficticio"
    });
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero); }
    private sealed class FakeHttp(HttpResponseMessage response) : HttpMessageHandler
    {
        public List<string> Scopes { get; } = [];
        public List<(HttpMethod Method, string Path, string? Body)> Operacoes { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.AbsolutePath == "/oauth/token")
            {
                Scopes.Add(Uri.UnescapeDataString(body!.Split('&').Single(x => x.StartsWith("scope=", StringComparison.Ordinal))[6..]));
                return Json("{\"access_token\":\"token-ficticio\",\"expires_in\":3600}");
            }
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Operacoes.Add((request.Method, request.RequestUri.AbsolutePath, body));
            return response;
        }
    }
}

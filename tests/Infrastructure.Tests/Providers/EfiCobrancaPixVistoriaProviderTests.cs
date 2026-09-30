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
        Assert.Equal(SituacaoConsultaPix.Confirmado,result.Situacao); Assert.Equal(E2e, result.Evento!.EndToEndId);
        Assert.Equal("pix.read", handler.Scopes.Single());
        Assert.Equal("/v2/pix/" + E2e, Assert.Single(handler.Operacoes).Path);
        Assert.DoesNotContain("SEGREDO_FICTICIO", result.ToString());
    }

    [Theory]
    [InlineData(400,SituacaoCobrancaProvider.BloqueioOperacional)]
    [InlineData(401,SituacaoCobrancaProvider.BloqueioOperacional)]
    [InlineData(403,SituacaoCobrancaProvider.BloqueioOperacional)]
    [InlineData(404,SituacaoCobrancaProvider.Indeterminada)]
    [InlineData(409,SituacaoCobrancaProvider.Conflito)]
    [InlineData(422,SituacaoCobrancaProvider.BloqueioOperacional)]
    [InlineData(429,SituacaoCobrancaProvider.Limitada)]
    [InlineData(500,SituacaoCobrancaProvider.Indisponivel)]
    [InlineData(503,SituacaoCobrancaProvider.Indisponivel)]
    public async Task ErroHttpClassificadoSemConfirmacaoOuRepeticaoIlimitada(int status,SituacaoCobrancaProvider esperado)
    {
        using var handler = new FakeHttp(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("SEGREDO_FICTICIO") });
        using var http = new HttpClient(handler);
        var result = await Provider(http).CriarAsync(Txid, 12.34m, 3600, default);
        Assert.Equal(esperado, result.Situacao);
        Assert.Equal(status==401 ? 2 : 1,handler.Operacoes.Count);
        Assert.Single(handler.Operacoes.Select(x=>x.Path).Distinct());
        Assert.DoesNotContain("SEGREDO_FICTICIO", result.ToString());
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

    [Theory]
    [InlineData("valor_invalido")]
    [InlineData("chave_invalida")]
    [InlineData("documento_bloqueado")]
    public async Task RejeicaoDocumentadaDeCriacaoEhDefinitivaSemRepeticao(string nome)
    {
        using var handler=new FakeHttp(new(HttpStatusCode.BadRequest) { Content=new StringContent(JsonSerializer.Serialize(new { nome, mensagem="SEGREDO_FICTICIO" })) });
        using var http=new HttpClient(handler);
        var resultado=await Provider(http).CriarAsync(Txid,12.34m,3600,default);
        Assert.Equal(SituacaoCobrancaProvider.FalhaDefinitiva,resultado.Situacao);
        Assert.Equal("criacao-rejeitada",resultado.Codigo);
        Assert.Single(handler.Operacoes);
        Assert.DoesNotContain("SEGREDO",resultado.ToString());
    }

    [Theory]
    [InlineData(404,SituacaoConsultaPix.AindaNaoDisponivel)]
    [InlineData(400,SituacaoConsultaPix.BloqueioOperacional)]
    [InlineData(401,SituacaoConsultaPix.BloqueioOperacional)]
    [InlineData(403,SituacaoConsultaPix.BloqueioOperacional)]
    [InlineData(422,SituacaoConsultaPix.BloqueioOperacional)]
    [InlineData(429,SituacaoConsultaPix.Indeterminado)]
    [InlineData(503,SituacaoConsultaPix.Indeterminado)]
    public async Task ConsultaE2eDistingueAusenciaBloqueioEIndeterminacao(int status,SituacaoConsultaPix esperado)
    {
        using var handler=new FakeHttp(new((HttpStatusCode)status) { Content=new StringContent("SEGREDO_FICTICIO") });
        using var http=new HttpClient(handler);
        var resultado=await Provider(http).ConsultarRecebimentoAsync(E2e,default);
        Assert.Equal(esperado,resultado.Situacao); Assert.Null(resultado.Evento);
        Assert.DoesNotContain("SEGREDO",resultado.ToString());
    }

    [Fact]
    public async Task UnauthorizedInvalidaSomenteEscopoRejeitadoRenovaUmaVezEMantemTxid()
    {
        var clock=new Clock(); var cache=new EfiPixAccessTokenCache(clock);
        await cache.ObterAsync("cob.write",_=>Task.FromResult(new EfiPixAccessToken("antigo",3600)),default);
        await cache.ObterAsync("cob.read",_=>Task.FromResult(new EfiPixAccessToken("leitura",3600)),default);
        using var handler=new AuthHandler(); using var http=new HttpClient(handler);
        var provider=new EfiCobrancaPixVistoriaProvider(http,
            new EfiPixOptions { Environment="Sandbox",BaseUrl="https://pix-h.api.efipay.com.br",ClientId="ficticio",ClientSecret="ficticio",CertificatePath="nao-carregado.p12" },
            new RecebimentoPixOptions { Habilitado=true,ChaveRecebedora=Chave },clock,cache);
        Assert.Equal(SituacaoCobrancaProvider.Ativa,(await provider.CriarAsync(Txid,12.34m,3600,default)).Situacao);
        Assert.Equal(1,handler.Autenticacoes);
        Assert.Equal(new[]{"antigo","novo"},handler.Tokens);
        Assert.All(handler.Paths,path=>Assert.Equal("/v2/cob/"+Txid,path));
        Assert.Equal(handler.Bodies[0],handler.Bodies[1]);
        Assert.Equal("leitura",await cache.ObterAsync("cob.read",_=>throw new InvalidOperationException("Nao renovar outro escopo"),default));
        await provider.CriarAsync(Txid,12.34m,3600,default);
        Assert.Equal(1,handler.Autenticacoes); // Token novo foi armazenado.
    }
    private sealed class AuthHandler : HttpMessageHandler
    {
        public int Autenticacoes;
        public List<string?> Tokens { get; }=[];
        public List<string> Paths { get; }=[];
        public List<string> Bodies { get; }=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.AbsolutePath=="/oauth/token")
            { Autenticacoes++; return Json("{\"access_token\":\"novo\",\"expires_in\":3600}"); }
            Tokens.Add(request.Headers.Authorization!.Parameter); Paths.Add(request.RequestUri.AbsolutePath);
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return Tokens[^1]=="antigo" ? new(HttpStatusCode.Unauthorized) : Json(Cobranca());
        }
    }

    [Fact]
    public async Task FalhaDeRedeEhIndeterminadaSemExporExcecao()
    {
        using var handler=new TransporteFalho { Rede=true }; using var http=new HttpClient(handler);
        var resultado=await Provider(http).ConsultarAsync(Txid,default);
        Assert.Equal(SituacaoCobrancaProvider.Indeterminada,resultado.Situacao);
        Assert.DoesNotContain("SEGREDO",resultado.ToString()); Assert.Equal(1,handler.Chamadas);
    }

    private sealed class TransporteFalho : HttpMessageHandler
    {
        public bool Rede { get; init; }
        public int Chamadas { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Chamadas++; if(Rede) throw new HttpRequestException("SEGREDO_FICTICIO"); throw new TaskCanceledException("SEGREDO_FICTICIO"); }
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
            return new HttpResponseMessage(response.StatusCode) { Content=new StringContent(await response.Content.ReadAsStringAsync(cancellationToken)) };
        }
    }
}

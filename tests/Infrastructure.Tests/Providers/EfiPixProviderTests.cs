using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Application.Interfaces.Providers;
using Domain.Enums;
using Infrastructure.Providers;
using Xunit;

namespace Infrastructure.Tests.Providers;

public sealed class EfiPixProviderTests
{
    [Fact]
    public void EfiPixHttpMessageHandlerFactory_DeveUsarDefaultKeySetENaoEphemeralKeySet()
    {
        var flags = EfiPixHttpMessageHandlerFactory.CertificateKeyStorageFlags;

        Assert.Equal(X509KeyStorageFlags.DefaultKeySet, flags);
        Assert.False(flags.HasFlag(X509KeyStorageFlags.EphemeralKeySet));
    }

    [Fact]
    public async Task EnviarAsync_DeveUsarV3ReferenciaEstavelPayloadCorretoEBearer()
    {
        var pagamentoPixId = Guid.Parse("94fd293e-8ed9-4672-9a07-63d4f1891c4d");
        var handler = new RoteadorHttpMessageHandler(
            OAuthComToken(),
            Json(HttpStatusCode.Created, """{"idEnvio":"94fd293e8ed946729a0763d4f1891c4d","status":"EM_PROCESSAMENTO"}"""));
        var provider = CriarProvider(handler);
        var request = new PixEnvioRequest(pagamentoPixId, 12.34m, TipoChavePix.Email, "favorecido@exemplo.com");

        var result = await provider.EnviarAsync(request);

        Assert.Equal(StatusPixProvider.Pendente, result.Status);
        Assert.Equal("/v3/gn/pix/94fd293e8ed946729a0763d4f1891c4d", handler.Requisicoes[1].PathAndQuery);
        Assert.Equal(HttpMethod.Put, handler.Requisicoes[1].Method);
        Assert.Equal("Bearer", handler.Requisicoes[1].AuthorizationScheme);
        Assert.Equal("token-ficticio", handler.Requisicoes[1].AuthorizationParameter);
        Assert.Equal(
            "{\"valor\":\"12.34\",\"pagador\":{\"chave\":\"pagador-ficticio@exemplo.com\"},\"favorecido\":{\"chave\":\"favorecido@exemplo.com\"}}",
            handler.Requisicoes[1].Body);
        Assert.Equal("pix.send", handler.Requisicoes[0].Form["scope"]);
        Assert.DoesNotContain("segredo-ficticio", handler.Requisicoes[0].AuthorizationParameter!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConsultarAsync_DeveUsarRotaOficialV2MesmaReferenciaEEscopoDeConsulta()
    {
        var pagamentoPixId = Guid.Parse("94fd293e-8ed9-4672-9a07-63d4f1891c4d");
        var handler = new RoteadorHttpMessageHandler(
            OAuthComToken(),
            Json(HttpStatusCode.OK, """{"idEnvio":"94fd293e8ed946729a0763d4f1891c4d","status":"REALIZADO"}"""));
        var provider = CriarProvider(handler);

        var result = await provider.ConsultarAsync(new PixConsultaRequest(pagamentoPixId));

        Assert.Equal(StatusPixProvider.Confirmado, result.Status);
        Assert.Equal("/v2/gn/pix/enviados/id-envio/94fd293e8ed946729a0763d4f1891c4d", handler.Requisicoes[1].PathAndQuery);
        Assert.Equal(HttpMethod.Get, handler.Requisicoes[1].Method);
        Assert.Equal("gn.pix.send.read", handler.Requisicoes[0].Form["scope"]);
    }

    [Theory]
    [InlineData("REALIZADO", StatusPixProvider.Confirmado)]
    [InlineData("REJEITADO", StatusPixProvider.FalhaConfirmada)]
    [InlineData("EM_PROCESSAMENTO", StatusPixProvider.Pendente)]
    [InlineData("STATUS_NOVO", StatusPixProvider.Indeterminado)]
    public async Task EnviarAsync_DeveTraduzirStatusDaEfi(string statusEfi, StatusPixProvider statusEsperado)
    {
        var handler = new RoteadorHttpMessageHandler(
            OAuthComToken(),
            Json(HttpStatusCode.Created, $$"""{"idEnvio":"referencia","status":"{{statusEfi}}"}"""));
        var provider = CriarProvider(handler);

        var result = await provider.EnviarAsync(CriarEnvioRequest());

        Assert.Equal(statusEsperado, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, StatusPixProvider.FalhaConfirmada)]
    [InlineData(HttpStatusCode.UnprocessableEntity, StatusPixProvider.FalhaConfirmada)]
    [InlineData(HttpStatusCode.Conflict, StatusPixProvider.Indeterminado)]
    [InlineData(HttpStatusCode.InternalServerError, StatusPixProvider.Indeterminado)]
    [InlineData(HttpStatusCode.GatewayTimeout, StatusPixProvider.Indeterminado)]
    public async Task EnviarAsync_DeveInterpretarHttpSemAssumirConfirmacaoIncorreta(
        HttpStatusCode httpStatus,
        StatusPixProvider statusEsperado)
    {
        var provider = CriarProvider(new RoteadorHttpMessageHandler(OAuthComToken(), new HttpResponseMessage(httpStatus)));

        var result = await provider.EnviarAsync(CriarEnvioRequest());

        Assert.Equal(statusEsperado, result.Status);
    }

    [Fact]
    public async Task EnviarAsync_TimeoutDeTransporte_DeveSerIndeterminado()
    {
        var provider = CriarProvider(new RoteadorHttpMessageHandler(
            OAuthComToken(),
            new OperationCanceledException()));

        var result = await provider.EnviarAsync(CriarEnvioRequest());

        Assert.Equal(StatusPixProvider.Indeterminado, result.Status);
        Assert.Equal("timeout", result.Codigo);
    }

    [Fact]
    public async Task EnviarAsync_RespostaInvalida_DeveSerIndeterminadoSemVazarChave()
    {
        const string chavePix = "chave-que-nao-pode-vazar@exemplo.com";
        var provider = CriarProvider(new RoteadorHttpMessageHandler(
            OAuthComToken(),
            Json(HttpStatusCode.Created, "conteudo-invalido")));

        var result = await provider.EnviarAsync(new PixEnvioRequest(
            Guid.NewGuid(), 1m, TipoChavePix.Email, chavePix));

        Assert.Equal(StatusPixProvider.Indeterminado, result.Status);
        Assert.Equal("invalid-response", result.Codigo);
        Assert.DoesNotContain(chavePix, result.IdentificadorProvider ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(chavePix, result.Codigo ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConsultarAsync_QuandoReferenciaNaoExiste_DeveRetornarFalhaConfirmada()
    {
        var provider = CriarProvider(new RoteadorHttpMessageHandler(
            OAuthComToken(),
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await provider.ConsultarAsync(new PixConsultaRequest(Guid.NewGuid()));

        Assert.Equal(StatusPixProvider.FalhaConfirmada, result.Status);
        Assert.Equal("http-404", result.Codigo);
    }

    [Fact]
    public async Task EnviarAsync_DeveReutilizarTokenValidoEmChamadasConcorrentes()
    {
        var respostas = new List<object> { OAuthComToken() };
        respostas.AddRange(Enumerable.Range(0, 10).Select(_ =>
            (object)Json(HttpStatusCode.Created, """{"status":"EM_PROCESSAMENTO"}""")));
        var handler = new RoteadorHttpMessageHandler([.. respostas]);
        var provider = CriarProvider(handler);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => provider.EnviarAsync(CriarEnvioRequest())));

        Assert.Equal(1, handler.Requisicoes.Count(requisicao => requisicao.PathAndQuery == "/oauth/token"));
    }

    [Fact]
    public async Task CacheDeToken_DeveRenovarAposExpiracao()
    {
        var timeProvider = new TimeProviderManual(DateTimeOffset.Parse("2026-08-31T00:00:00Z"));
        var cache = new EfiPixAccessTokenCache(timeProvider);
        var quantidadeDeRenovacoes = 0;

        var primeiro = await cache.ObterAsync("pix.send", _ => Task.FromResult(
            new EfiPixAccessToken($"token-{++quantidadeDeRenovacoes}", 3600)), CancellationToken.None);
        timeProvider.Avancar(TimeSpan.FromHours(2));
        var segundo = await cache.ObterAsync("pix.send", _ => Task.FromResult(
            new EfiPixAccessToken($"token-{++quantidadeDeRenovacoes}", 3600)), CancellationToken.None);

        Assert.Equal("token-1", primeiro);
        Assert.Equal("token-2", segundo);
        Assert.Equal(2, quantidadeDeRenovacoes);
    }

    [Fact]
    public async Task EnviarAsync_DevePropagarCancellationTokenSolicitadoExternamente()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var provider = CriarProvider(new RoteadorHttpMessageHandler(OAuthComToken()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.EnviarAsync(CriarEnvioRequest(), cancellationTokenSource.Token));
    }

    [Fact]
    public void EfiPixOptions_DeveBloquearProducaoSemIncluirSegredosNaExcecao()
    {
        var options = new EfiPixOptions
        {
            Environment = "Production",
            BaseUrl = "https://pix-h.api.efipay.com.br",
            ClientId = "client-id-ficticio",
            ClientSecret = "segredo-que-nao-pode-vazar",
            CertificatePath = "certificado-ficticio.p12",
            ChavePixPagador = "pagador-ficticio@exemplo.com"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.ValidarParaSandbox());

        Assert.DoesNotContain("segredo-que-nao-pode-vazar", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Sandbox", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HandlerMtls_QuandoCertificadoNaoExiste_NaoDeveExporOCaminhoNaExcecao()
    {
        const string certificatePath = "C:\\diretorio-privado\\certificado-ausente.p12";
        var options = CriarOptionsComCertificatePath(certificatePath);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EfiPixHttpMessageHandlerFactory.Criar(options));

        Assert.DoesNotContain(certificatePath, exception.Message, StringComparison.Ordinal);
    }

    private static EfiPixProvider CriarProvider(RoteadorHttpMessageHandler handler) =>
        new(new HttpClient(handler), CriarOptions());

    private static EfiPixOptions CriarOptions() => new()
    {
        Environment = "Sandbox",
        BaseUrl = "https://pix-h.api.efipay.com.br",
        ClientId = "client-id-ficticio",
        ClientSecret = "segredo-ficticio",
        CertificatePath = "certificado-ficticio.p12",
        CertificatePassword = "senha-ficticia",
        ChavePixPagador = "pagador-ficticio@exemplo.com"
    };

    private static EfiPixOptions CriarOptionsComCertificatePath(string certificatePath) => new()
    {
        Environment = "Sandbox",
        BaseUrl = "https://pix-h.api.efipay.com.br",
        ClientId = "client-id-ficticio",
        ClientSecret = "segredo-ficticio",
        CertificatePath = certificatePath,
        ChavePixPagador = "pagador-ficticio@exemplo.com"
    };

    private static PixEnvioRequest CriarEnvioRequest() =>
        new(Guid.NewGuid(), 1.23m, TipoChavePix.Email, "favorecido@exemplo.com");

    private static HttpResponseMessage OAuthComToken() =>
        Json(HttpStatusCode.OK, """{"access_token":"token-ficticio","expires_in":3600}""");

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) =>
        new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private sealed class RoteadorHttpMessageHandler(params object[] respostas) : HttpMessageHandler
    {
        private readonly object _sync = new();
        private readonly Queue<object> _respostas = new(respostas);

        public List<RequisicaoCapturada> Requisicoes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var form = request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded"
                ? body!
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(par => par.Split('=', 2))
                    .ToDictionary(par => Uri.UnescapeDataString(par[0]), par => Uri.UnescapeDataString(par[1]))
                : [];

            object resposta;
            lock (_sync)
            {
                Requisicoes.Add(new RequisicaoCapturada(
                    request.Method,
                    request.RequestUri!.PathAndQuery,
                    request.Headers.Authorization?.Scheme,
                    request.Headers.Authorization?.Parameter,
                    body,
                    form));
                resposta = _respostas.Dequeue();
            }

            if (resposta is Exception exception)
                throw exception;

            return (HttpResponseMessage)resposta;
        }
    }

    private sealed record RequisicaoCapturada(
        HttpMethod Method,
        string PathAndQuery,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body,
        Dictionary<string, string> Form);

    private sealed class TimeProviderManual(DateTimeOffset agora) : TimeProvider
    {
        private DateTimeOffset _agora = agora;

        public override DateTimeOffset GetUtcNow() => _agora;

        public void Avancar(TimeSpan intervalo) => _agora = _agora.Add(intervalo);
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Jornada;
using Application.Recebimentos;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Integration;

public sealed class IndicacaoPublicaPipelineTests(ApiTestWebApplicationFactory factory) : IClassFixture<ApiTestWebApplicationFactory>
{
    [Fact]
    public async Task CodigoPublicoRetornaSomenteUsabilidadeSemIdentidade()
    {
        var store=new Mock<IJornadaPublicaStore>(MockBehavior.Strict);
        store.Setup(x=>x.CodigoUtilizavelAsync("ABCD1234",It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddScoped(_=>store.Object)));
        using var client=app.CreateHttpsClient();
        using var resposta=await client.GetAsync("/api/public/indicacoes/codigos/abcd1234");
        Assert.Equal(HttpStatusCode.OK,resposta.StatusCode);
        using var json=JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Equal(new[]{"utilizavel"},json.RootElement.EnumerateObject().Select(p=>p.Name));
        Assert.True(resposta.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer",resposta.Headers.GetValues("Referrer-Policy").Single());
    }
    [Fact]
    public async Task ConsentimentoAusenteOuCampoDeUsuarioArbitrarioNaoChegaAoStore()
    {
        var store=new Mock<IJornadaPublicaStore>(MockBehavior.Strict);
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddScoped(_=>store.Object)));
        using var client=app.CreateHttpsClient(); client.DefaultRequestHeaders.Add("Idempotency-Key",Guid.NewGuid().ToString());
        using var semAceite=await client.PostAsJsonAsync("/api/public/indicacoes",new IndicacaoPublicaRequest("ABCD1234","Pessoa Ficticia","85999990000",JornadaValidacao.VersaoTermo,false));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,semAceite.StatusCode);
        using var campoExtra=await client.PostAsJsonAsync("/api/public/indicacoes",new {codigo="ABCD1234",nome="Pessoa",telefone="85999990000",versaoTermo=JornadaValidacao.VersaoTermo,consentimento=true,usuarioId=Guid.NewGuid()});
        Assert.Equal(HttpStatusCode.BadRequest,campoExtra.StatusCode);
        store.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task CaptacaoNormalizaEDevolveSomenteProtocolo()
    {
        var store=new Mock<IJornadaPublicaStore>(MockBehavior.Strict);
        var chave=Guid.NewGuid().ToString(); var protocolo=new string('a',64);
        store.Setup(x=>x.CaptarAsync(It.Is<IndicacaoPublicaRequest>(r=>r.Nome=="Pessoa Ficticia" && r.Telefone=="85999990000" && r.Codigo=="ABCD1234"),chave,It.Is<DateTime>(d=>d.Kind==DateTimeKind.Utc),It.IsAny<CancellationToken>())).ReturnsAsync(new ProtocoloIndicacao(protocolo));
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddScoped(_=>store.Object)));
        using var client=app.CreateHttpsClient(); client.DefaultRequestHeaders.Add("Idempotency-Key",chave);
        using var response=await client.PostAsJsonAsync("/api/public/indicacoes",new IndicacaoPublicaRequest("abcd1234"," Pessoa Ficticia ","(85) 99999-0000",JornadaValidacao.VersaoTermo,true));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[]{"protocolo"},json.RootElement.EnumerateObject().Select(p=>p.Name));
        Assert.Equal(protocolo,json.RootElement.GetProperty("protocolo").GetString());
        store.VerifyAll();
    }
    [Fact]
    public async Task RateLimitRejeitaAntesDoStoreSemProviderOuDadosSensiveis()
    {
        var store=new Mock<IJornadaPublicaStore>(MockBehavior.Strict);
        store.Setup(x=>x.CodigoUtilizavelAsync("ABCD1234",It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var provider=new Mock<ICobrancaPixVistoriaProvider>(MockBehavior.Strict);
        using var logs=new Captura();
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>
        {s.AddScoped(_=>store.Object);s.AddScoped(_=>provider.Object);s.AddSingleton<ILoggerProvider>(logs);}));
        using var client=app.CreateHttpsClient();
        for(var i=0;i<10;i++)
        { using var response=await client.GetAsync("/api/public/indicacoes/codigos/ABCD1234"); Assert.Equal(HttpStatusCode.NotFound,response.StatusCode); }
        const string segredo="CORPO_FICTICIO_NAO_LOGAR";
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/public/indicacoes") {Content=new StringContent(segredo)};
        request.Headers.Add("Idempotency-Key","TOKEN_FICTICIO_NAO_LOGAR");
        using var rejeitada=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TooManyRequests,rejeitada.StatusCode);
        Assert.Empty(await rejeitada.Content.ReadAsStringAsync());
        Assert.True(rejeitada.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer",rejeitada.Headers.GetValues("Referrer-Policy").Single());
        Assert.InRange(rejeitada.Headers.RetryAfter!.Delta!.Value.TotalSeconds,1,60);
        store.Verify(x=>x.CodigoUtilizavelAsync("ABCD1234",It.IsAny<CancellationToken>()),Times.Exactly(10));
        store.VerifyNoOtherCalls(); provider.VerifyNoOtherCalls();
        Assert.DoesNotContain(logs.Mensagens,m=>m.Contains(segredo)||m.Contains("TOKEN_FICTICIO_NAO_LOGAR"));
    }
    [Fact]
    public async Task FalhaInternaNaoEcoaMensagemOuSqlNosLogsEResposta()
    {
        const string segredo="SEGREDO_FICTICIO_SELECT_TELEFONE";
        var store=new Mock<IJornadaPublicaStore>();
        store.Setup(x=>x.CodigoUtilizavelAsync(It.IsAny<string>(),It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException(segredo));
        using var logs=new Captura();
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{s.AddScoped(_=>store.Object);s.AddSingleton<ILoggerProvider>(logs);}));
        using var client=app.CreateHttpsClient(); using var response=await client.GetAsync("/api/public/indicacoes/codigos/ABCD1234");
        Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);
        Assert.DoesNotContain(segredo,await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(logs.Mensagens,m=>m.Contains(segredo));
    }
    private sealed class Captura : ILoggerProvider
    {
        public ConcurrentBag<string> Mensagens {get;}=[];
        public ILogger CreateLogger(string categoryName)=>new Logger(Mensagens);
        public void Dispose(){}
        private sealed class Logger(ConcurrentBag<string> mensagens):ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
            public bool IsEnabled(LogLevel logLevel)=>true;
            public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>mensagens.Add(formatter(state,exception)+exception);
        }
    }
}

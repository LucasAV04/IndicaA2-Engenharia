using System.Net;
using Application.Recebimentos;
using Domain.Enums;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Moq;
using Xunit;

namespace API.Tests.Integration;

public sealed class CobrancaPublicaPipelineTests(ApiTestWebApplicationFactory factory) : IClassFixture<ApiTestWebApplicationFactory>
{
    [Fact]
    public async Task TokenNoHeaderRetornaSomenteContratoMinimoSemCache()
    {
        var token=new string('A',64);
        var service=new Mock<ICobrancaPixVistoriaService>();
        service.Setup(x=>x.ObterPublicaAsync(token,It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CobrancaPublica(12.34m,StatusCobrancaPixVistoria.Ativa,null,"CODIGO_FICTICIO",false));
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>
        { s.AddSingleton(new RecebimentoPixOptions { Habilitado=true }); s.AddScoped(_=>service.Object); }));
        using var client=app.CreateHttpsClient(); client.DefaultRequestHeaders.Authorization=new("PaymentLink",token);
        using var response=await client.GetAsync("/api/public/cobranca-pix-vistoria");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer",response.Headers.GetValues("Referrer-Policy").Single());
        using var body=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[]{"valor","status","venceEm","pixCopiaECola","confirmada"},body.RootElement.EnumerateObject().Select(p=>p.Name));
        service.Verify(x=>x.ObterPublicaAsync(token,It.IsAny<CancellationToken>()),Times.Once);
    }

    [Theory]
    [InlineData("", false)] [InlineData("Bearer abc", false)] [InlineData("", true)]
    public async Task TokenAusenteInvalidoOuSomenteNaQueryNaoAutoriza(string auth,bool query)
    {
        var service=new Mock<ICobrancaPixVistoriaService>(MockBehavior.Strict);
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>
        { s.AddSingleton(new RecebimentoPixOptions { Habilitado=true }); s.AddScoped(_=>service.Object); }));
        using var client=app.CreateHttpsClient();
        if(auth!="") client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization",auth);
        using var response=await client.GetAsync("/api/public/cobranca-pix-vistoria"+(query?"?token="+new string('A',64):""));
        Assert.Equal(HttpStatusCode.NotFound,response.StatusCode); service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RateLimitRejeitaExcessoSemConsultarStoreNovamente()
    {
        var service=new Mock<ICobrancaPixVistoriaService>();
        var store=new Mock<ICobrancaPixVistoriaStore>(MockBehavior.Strict);
        var provider=new Mock<ICobrancaPixVistoriaProvider>(MockBehavior.Strict);
        using var logs=new CapturedLogs();
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>
        {
            s.AddSingleton(new RecebimentoPixOptions { Habilitado=true });
            s.AddScoped(_=>service.Object);
            s.AddScoped(_=>store.Object);
            s.AddScoped(_=>provider.Object);
            s.AddSingleton<ILoggerProvider>(logs);
        }));
        var token=new string('B',64);
        const string sensitiveBody="CORPO_SENSIVEL_FICTICIO_NAO_LOGAR";
        using var client=app.CreateHttpsClient(); client.DefaultRequestHeaders.Authorization=new("PaymentLink",token);
        for(var n=0;n<30;n++)
        { using var resposta=await client.GetAsync("/api/public/cobranca-pix-vistoria"); Assert.Equal(HttpStatusCode.NotFound,resposta.StatusCode); }
        using var request=new HttpRequestMessage(HttpMethod.Get,"/api/public/cobranca-pix-vistoria")
        { Content=new StringContent(sensitiveBody) };
        using var bloqueada=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TooManyRequests,bloqueada.StatusCode);
        Assert.Empty(await bloqueada.Content.ReadAsStringAsync());
        Assert.True(bloqueada.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer",bloqueada.Headers.GetValues("Referrer-Policy").Single());
        Assert.NotNull(bloqueada.Headers.RetryAfter?.Delta);
        Assert.InRange(bloqueada.Headers.RetryAfter!.Delta!.Value.TotalSeconds,1,60);
        service.Verify(x=>x.ObterPublicaAsync(It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Exactly(30));
        store.VerifyNoOtherCalls();
        provider.VerifyNoOtherCalls();
        Assert.NotEmpty(logs.Messages);
        Assert.DoesNotContain(logs.Messages,message=>message.Contains(token,StringComparison.Ordinal)
            || message.Contains(sensitiveBody,StringComparison.Ordinal));
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; }=[];
        public ILogger CreateLogger(string categoryName)=>new CaptureLogger(Messages);
        public void Dispose() { }
        private sealed class CaptureLogger(ConcurrentBag<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
            public bool IsEnabled(LogLevel logLevel)=>true;
            public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,
                Func<TState,Exception?,string> formatter)=>messages.Add(formatter(state,exception)+exception);
        }
    }
}

using System.Collections.Concurrent;
using System.Threading.Channels;
using API.Processing;
using API.Tests.Integration;
using Application.Interfaces.Providers;
using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Application.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace API.Tests.Processing;

public sealed class PagamentoPixWorkerHospedadoTests
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Hospedado_PrimeiroTickIniciaUmCicloEFalhaDoSeletorAguardaProximoTick()
    {
        using var c = new Contexto();
        c.Seletor.SetupSequence(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SEGREDO-SELETOR"))
            .ReturnsAsync(Array.Empty<Guid>());
        await c.Worker.StartAsync(default);
        try
        {
            await c.Ticks.EsperarAguardandoAsync();
            c.Seletor.VerifyNoOtherCalls();
            c.Ticks.Tick();
            await c.Ticks.EsperarAguardandoAsync();
            c.Seletor.Verify(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>()), Times.Once);
            Assert.False(c.Worker.ExecuteTask!.IsCompleted);
            Assert.Single(c.Logs.Erros);
            Assert.DoesNotContain("SEGREDO-SELETOR", c.Logs.Texto);
            Assert.All(c.Logs.Entries, e => Assert.Null(e.Exception));
            c.Ticks.Tick();
            await c.Ticks.EsperarAguardandoAsync();
            c.Seletor.Verify(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
        finally { await c.EncerrarAsync(); }
        Assert.True(c.Worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Hospedado_TicksDurantePagamentoNaoSobrepoemCiclosEItensSaoSequenciais()
    {
        using var c = new Contexto();
        var primeiro = Guid.NewGuid();
        var segundo = Guid.NewGuid();
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Seletor.SetupSequence(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { primeiro, primeiro, segundo })
            .ReturnsAsync(Array.Empty<Guid>());
        c.Processador.Setup(x => x.ProcessarAsync(primeiro, It.IsAny<CancellationToken>()))
            .Returns(async () => { entrou.TrySetResult(); await liberar.Task.WaitAsync(Limite); return Resultado(primeiro); });
        c.Processador.Setup(x => x.ProcessarAsync(segundo, It.IsAny<CancellationToken>())).ReturnsAsync(Resultado(segundo));
        await c.Worker.StartAsync(default);
        try
        {
            await c.Ticks.EsperarAguardandoAsync();
            c.Ticks.Tick();
            await entrou.Task.WaitAsync(Limite);
            c.Ticks.Tick();
            c.Seletor.Verify(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>()), Times.Once);
            c.Processador.Verify(x => x.ProcessarAsync(segundo, It.IsAny<CancellationToken>()), Times.Never);
            liberar.TrySetResult();
            await c.Ticks.EsperarAguardandoAsync();
            await c.Ticks.EsperarAguardandoAsync();
            c.Seletor.Verify(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>()), Times.Exactly(2));
            c.Processador.Verify(x => x.ProcessarAsync(primeiro, It.IsAny<CancellationToken>()), Times.Once);
            c.Processador.Verify(x => x.ProcessarAsync(segundo, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { liberar.TrySetResult(); await c.EncerrarAsync(); }
    }

    [Fact]
    public async Task Hospedado_CancelamentoDuranteEsperaEncerraComSucessoSemErro()
    {
        using var c = new Contexto();
        await c.Worker.StartAsync(default);
        try { await c.Ticks.EsperarAguardandoAsync(); }
        finally { await c.EncerrarAsync(); }
        Assert.True(c.Worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Empty(c.Logs.Erros);
        c.Seletor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hospedado_CancelamentoDuranteItemNaoIniciaProximoNemRegistraErro(bool lancarCancelamento)
    {
        using var c = new Contexto();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Seletor.Setup(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(ids);
        c.Processador.Setup(x => x.ProcessarAsync(ids[0], It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, CancellationToken ct) =>
            {
                var cancelou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registro = ct.Register(() => cancelou.TrySetResult());
                entrou.TrySetResult();
                await cancelou.Task.WaitAsync(Limite);
                if (lancarCancelamento) ct.ThrowIfCancellationRequested();
                return Resultado(id);
            });
        await c.Worker.StartAsync(default);
        try { await c.Ticks.EsperarAguardandoAsync(); c.Ticks.Tick(); await entrou.Task.WaitAsync(Limite); }
        finally { await c.EncerrarAsync(); }
        Assert.True(c.Worker.ExecuteTask!.IsCompletedSuccessfully);
        c.Processador.Verify(x => x.ProcessarAsync(ids[1], It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(c.Logs.Erros);
    }

    [Fact]
    public async Task Hospedado_DesabilitadoNaoCriaEscopoNemTemporizador()
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        using var worker = new PagamentoPixProcessamentoWorker(scopes.Object,
            Options.Create(new PagamentoPixProcessamentoWorkerOptions()), new Logs(),
            _ => throw new Xunit.Sdk.XunitException("Não deve criar timer"));
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(Limite);
        await worker.ExecutarCicloAsync(default);
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HostedService_ConfiguracaoHabilitadaInvalidaFalhaNoStartup()
    {
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddSingleton<IOptions<PagamentoPixProcessamentoWorkerOptions>>(Options.Create(
                new PagamentoPixProcessamentoWorkerOptions { Habilitado = true, IntervaloSegundos = 4 }));
            services.AddHostedService<PagamentoPixProcessamentoWorker>();
        }).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task Api_DesabilitadaIniciaSemResolverSeletorOuProviderEProcessadorContinuaScoped()
    {
        using var factoryBase = new ApiTestWebApplicationFactory();
        using var factory = factoryBase.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            Assert.Equal(ServiceLifetime.Scoped,
                services.Last(d => d.ServiceType == typeof(IPagamentoPixProcessamentoService)).Lifetime);
            services.AddScoped<IPagamentoPixCandidatoProcessamentoStore>(_ => throw new Xunit.Sdk.XunitException("Seletor resolvido"));
            services.AddScoped<IPixProvider>(_ => throw new Xunit.Sdk.XunitException("Provider resolvido"));
            services.PostConfigure<PagamentoPixProcessamentoWorkerOptions>(o => Assert.False(o.Habilitado));
        }));
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        Assert.Single(factory.Services.GetServices<IHostedService>().OfType<PagamentoPixProcessamentoWorker>());
        var dependencias = typeof(PagamentoPixProcessamentoWorker).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(dependencias, p => p.ParameterType == typeof(IPixProvider)
            || p.ParameterType == typeof(IPagamentoPixCandidatoProcessamentoStore)
            || p.ParameterType == typeof(IPagamentoPixProcessamentoService));
    }

    private static ResultadoProcessamentoPagamentoPix Resultado(Guid id) =>
        ResultadoProcessamentoPagamentoPix.Criar(id, StatusProcessamentoPagamentoPix.Terminal);

    private sealed class Ticks : IProcessamentoPixTicks
    {
        private readonly Channel<bool> _ticks = Channel.CreateUnbounded<bool>();
        private readonly Channel<bool> _esperas = Channel.CreateUnbounded<bool>();
        public async ValueTask<bool> AguardarAsync(CancellationToken ct)
        { _esperas.Writer.TryWrite(true); return await _ticks.Reader.ReadAsync(ct); }
        public void Tick() => _ticks.Writer.TryWrite(true);
        public async Task EsperarAguardandoAsync() => await _esperas.Reader.ReadAsync().AsTask().WaitAsync(Limite);
        public void Dispose() => _ticks.Writer.TryComplete();
    }

    private sealed class Logs : ILogger<PagamentoPixProcessamentoWorker>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();
        public IEnumerable<string> Erros => Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message);
        public string Texto => string.Join("\n", Entries.Select(e => e.Message));
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((level, formatter(state, ex), ex));
    }

    private sealed class Contexto : IDisposable
    {
        public Mock<IPagamentoPixCandidatoProcessamentoStore> Seletor { get; } = new(MockBehavior.Strict);
        public Mock<IPagamentoPixProcessamentoService> Processador { get; } = new(MockBehavior.Strict);
        public Ticks Ticks { get; } = new();
        public Logs Logs { get; } = new();
        private readonly ServiceProvider _services;
        public PagamentoPixProcessamentoWorker Worker { get; }
        public Contexto()
        {
            _services = new ServiceCollection().AddScoped(_ => Seletor.Object).AddScoped(_ => Processador.Object)
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            Worker = new(_services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new PagamentoPixProcessamentoWorkerOptions { Habilitado = true }), Logs, _ => Ticks);
        }
        public async Task EncerrarAsync()
        {
            await Worker.StopAsync(default).WaitAsync(Limite);
            if (Worker.ExecuteTask is not null) await Worker.ExecuteTask.WaitAsync(Limite);
        }
        public void Dispose() { Worker.Dispose(); _services.Dispose(); }
    }
}

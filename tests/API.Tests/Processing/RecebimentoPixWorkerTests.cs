using System.Threading.Channels;
using API.Processing;
using Application.Recebimentos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Processing;

public sealed class RecebimentoPixWorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DesabilitadoNaoCriaEscopoNemTimer()
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        using var worker = new RecebimentoPixVistoriaWorker(scopes.Object,new(),Mock.Of<ILogger<RecebimentoPixVistoriaWorker>>(),_=>throw new InvalidOperationException("timer-proibido"));
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(Timeout);
        await worker.CicloAsync(default);
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PrimeiroTickENovoTickAposFalhaDoSeletorSemRetryImediato()
    {
        using var c = new Cenario();
        c.Seletor.SetupSequence(x=>x.EventosAsync(2,It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SEGREDO_FICTICIO"))
            .ReturnsAsync(Array.Empty<Guid>());
        await c.Worker.StartAsync(default);
        try
        {
            await c.Ticks.Aguardando(); c.Seletor.VerifyNoOtherCalls();
            c.Ticks.Tick(); await c.Ticks.Aguardando();
            c.Seletor.Verify(x=>x.EventosAsync(2,It.IsAny<CancellationToken>()),Times.Once);
            Assert.False(c.Worker.ExecuteTask!.IsCompleted);
            Assert.DoesNotContain("SEGREDO_FICTICIO",string.Join('\n',c.Logs.Textos));
            Assert.Single(c.Logs.Textos);
            c.Ticks.Tick(); await c.Ticks.Aguardando();
            c.Seletor.Verify(x=>x.EventosAsync(2,It.IsAny<CancellationToken>()),Times.Exactly(2));
        }
        finally { await c.Parar(); }
    }

    [Fact]
    public async Task LoteUnicoSequencialDeduplicaEventosEUsaRestanteParaCobrancas()
    {
        using var c = new Cenario(); var evento=Guid.NewGuid(); var cobranca=Guid.NewGuid();
        c.Seletor.Setup(x=>x.EventosAsync(2,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{evento,evento});
        c.Seletor.Setup(x=>x.CobrançasAsync(1,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{cobranca,Guid.NewGuid()});
        await c.Worker.CicloAsync(default);
        c.Processador.Verify(x=>x.ProcessarEventoAsync(evento,It.IsAny<CancellationToken>()),Times.Once);
        c.Processador.Verify(x=>x.ProcessarCobrancaAsync(cobranca,It.IsAny<CancellationToken>()),Times.Once);
        Assert.Equal(2,c.Processador.Invocations.Count);
    }

    [Fact]
    public async Task FalhaDeItemNaoImpedeProximoENaoLogaExcecaoOuMensagem()
    {
        using var c = new Cenario(); var ids=new[]{Guid.NewGuid(),Guid.NewGuid()};
        c.Seletor.Setup(x=>x.EventosAsync(2,It.IsAny<CancellationToken>())).ReturnsAsync(ids);
        c.Processador.Setup(x=>x.ProcessarEventoAsync(ids[0],It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("SEGREDO_FICTICIO"));
        await c.Worker.CicloAsync(default);
        c.Processador.Verify(x=>x.ProcessarEventoAsync(ids[1],It.IsAny<CancellationToken>()),Times.Once);
        c.Seletor.Verify(x=>x.CobrançasAsync(It.IsAny<int>(),It.IsAny<CancellationToken>()),Times.Never);
        Assert.Single(c.Logs.Textos); Assert.DoesNotContain("SEGREDO_FICTICIO",c.Logs.Textos[0]);
    }

    [Fact]
    public async Task TickDuranteItemNaoSobrepoeCicloETarefasSempreSaoObservadas()
    {
        using var c = new Cenario(); var id=Guid.NewGuid();
        var entrou=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Seletor.SetupSequence(x=>x.EventosAsync(2,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{id}).ReturnsAsync(Array.Empty<Guid>());
        c.Processador.Setup(x=>x.ProcessarEventoAsync(id,It.IsAny<CancellationToken>())).Returns(async()=>{ entrou.TrySetResult(); await liberar.Task.WaitAsync(Timeout); });
        await c.Worker.StartAsync(default);
        try
        {
            await c.Ticks.Aguardando(); c.Ticks.Tick(); await entrou.Task.WaitAsync(Timeout);
            c.Ticks.Tick(); c.Seletor.Verify(x=>x.EventosAsync(2,It.IsAny<CancellationToken>()),Times.Once);
            liberar.TrySetResult(); await c.Ticks.Aguardando(); await c.Ticks.Aguardando();
            c.Processador.Verify(x=>x.ProcessarEventoAsync(id,It.IsAny<CancellationToken>()),Times.Once);
        }
        finally { liberar.TrySetResult(); await c.Parar(); }
    }

    [Fact]
    public async Task CancelamentoDaEsperaEncerraSemErro()
    {
        using var c = new Cenario(); await c.Worker.StartAsync(default);
        try { await c.Ticks.Aguardando(); }
        finally { await c.Parar(); }
        Assert.Empty(c.Logs.Textos); Assert.True(c.Worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Empty(c.Processador.Invocations);
    }

    [Fact]
    public async Task CancelamentoDuranteItemNaoIniciaProximo()
    {
        using var c = new Cenario(); var ids=new[]{Guid.NewGuid(),Guid.NewGuid()};
        var entrou=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Seletor.Setup(x=>x.EventosAsync(2,It.IsAny<CancellationToken>())).ReturnsAsync(ids);
        c.Processador.Setup(x=>x.ProcessarEventoAsync(ids[0],It.IsAny<CancellationToken>())).Returns(async(Guid _,CancellationToken ct)=>
        {
            var cancelado=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registro=ct.Register(()=>cancelado.TrySetResult()); entrou.TrySetResult();
            await cancelado.Task.WaitAsync(Timeout); ct.ThrowIfCancellationRequested();
        });
        await c.Worker.StartAsync(default);
        try { await c.Ticks.Aguardando(); c.Ticks.Tick(); await entrou.Task.WaitAsync(Timeout); }
        finally { await c.Parar(); }
        c.Processador.Verify(x=>x.ProcessarEventoAsync(ids[1],It.IsAny<CancellationToken>()),Times.Never);
        Assert.Empty(c.Logs.Textos);
    }

    private sealed class Cenario : IDisposable
    {
        public Mock<IRecebimentoPixCandidatoStore> Seletor { get; }=new();
        public Mock<IRecebimentoPixProcessamentoService> Processador { get; }=new();
        public Ticks Ticks { get; }=new(); public Logs Logs { get; }=new();
        private readonly ServiceProvider _services;
        public RecebimentoPixVistoriaWorker Worker { get; }
        public Cenario()
        {
            Seletor.Setup(x=>x.EventosAsync(It.IsAny<int>(),It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Guid>());
            Seletor.Setup(x=>x.CobrançasAsync(It.IsAny<int>(),It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Guid>());
            _services=new ServiceCollection().AddScoped(_=>Seletor.Object).AddScoped(_=>Processador.Object).BuildServiceProvider();
            Worker=new(_services.GetRequiredService<IServiceScopeFactory>(),new RecebimentoPixOptions { Habilitado=true,ProcessamentoWorker=new(){Habilitado=true,TamanhoLote=2} },Logs,_=>Ticks);
        }
        public async Task Parar() { using var ct=new CancellationTokenSource(Timeout); await Worker.StopAsync(ct.Token); await Worker.ExecuteTask!.WaitAsync(Timeout); }
        public void Dispose() { Worker.Dispose(); _services.Dispose(); }
    }
    private sealed class Ticks : IProcessamentoPixTicks
    {
        private readonly Channel<bool> _ticks=Channel.CreateUnbounded<bool>();
        private readonly Channel<bool> _esperas=Channel.CreateUnbounded<bool>();
        public void Tick()=>_ticks.Writer.TryWrite(true);
        public async Task Aguardando()=>await _esperas.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        public async ValueTask<bool> AguardarAsync(CancellationToken ct) { _esperas.Writer.TryWrite(true); return await _ticks.Reader.ReadAsync(ct); }
        public void Dispose() { _ticks.Writer.TryComplete(); _esperas.Writer.TryComplete(); }
    }
    private sealed class Logs : ILogger<RecebimentoPixVistoriaWorker>
    {
        public List<string> Textos { get; }=[];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull => null;
        public bool IsEnabled(LogLevel logLevel)=>true;
        public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)
        { Assert.Null(exception); Textos.Add(formatter(state,exception)); }
    }
}

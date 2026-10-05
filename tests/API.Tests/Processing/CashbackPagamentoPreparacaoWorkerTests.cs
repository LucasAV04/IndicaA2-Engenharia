using System.Threading.Channels;
using API.Processing;
using Application.Jornada;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Processing;

public sealed class CashbackPagamentoPreparacaoWorkerTests
{
    [Theory]
    [InlineData(4,20)] [InlineData(3601,20)] [InlineData(60,0)] [InlineData(60,101)]
    public void ConfiguracaoInvalidaFalhaSomenteQuandoHabilitado(int intervalo,int lote)
    {
        var options=new CashbackPreparacaoOptions {IntervaloSegundos=intervalo,TamanhoLote=lote};
        options.Validar(); options.Habilitado=true;
        Assert.Throws<InvalidOperationException>(options.Validar);
    }
    [Fact]
    public async Task DesabilitadoNaoCriaEscopoNemTimer()
    {
        var scopes=new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        using var worker=new CashbackPagamentoPreparacaoWorker(scopes.Object,new(),NullLogger<CashbackPagamentoPreparacaoWorker>.Instance,_=>throw new Exception("Não deve criar timer"));
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.Ciclo(CancellationToken.None);
        scopes.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task CicloLimitaLoteRemoveDuplicadosEContinuaAposFalha()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();
        var store=new Mock<IJornadaFinanceiraStore>(MockBehavior.Strict);
        store.Setup(x=>x.CandidatosAsync(2,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{a,a,b,c});
        store.Setup(x=>x.PrepararCashbackAsync(a,It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("FICTICIO"));
        store.Setup(x=>x.PrepararCashbackAsync(b,It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var services=new ServiceCollection().AddScoped(_=>store.Object).BuildServiceProvider();
        using var worker=new CashbackPagamentoPreparacaoWorker(services.GetRequiredService<IServiceScopeFactory>(),new(){Habilitado=true,TamanhoLote=2},NullLogger<CashbackPagamentoPreparacaoWorker>.Instance);
        await worker.Ciclo(CancellationToken.None);
        store.Verify(x=>x.PrepararCashbackAsync(a,It.IsAny<CancellationToken>()),Times.Once);
        store.Verify(x=>x.PrepararCashbackAsync(b,It.IsAny<CancellationToken>()),Times.Once);
        store.Verify(x=>x.PrepararCashbackAsync(c,It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task PrimeiroTickObrigatorioSemSobreposicaoECancelamentoObservado()
    {
        var id=Guid.NewGuid();var entrou=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var segundo=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamadas=0; var ativos=0;var maximo=0;
        var store=new Mock<IJornadaFinanceiraStore>(MockBehavior.Strict);
        store.Setup(x=>x.CandidatosAsync(20,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{id});
        store.Setup(x=>x.PrepararCashbackAsync(id,It.IsAny<CancellationToken>())).Returns(async (Guid _,CancellationToken ct)=>
        {
            maximo=Math.Max(maximo,Interlocked.Increment(ref ativos));
            try
            {
                if(Interlocked.Increment(ref chamadas)==1){entrou.TrySetResult();await liberar.Task.WaitAsync(ct);}
                else segundo.TrySetResult();
            }
            finally {Interlocked.Decrement(ref ativos);}
        });
        using var services=new ServiceCollection().AddScoped(_=>store.Object).BuildServiceProvider();
        using var ticks=new Ticks();
        using var worker=new CashbackPagamentoPreparacaoWorker(services.GetRequiredService<IServiceScopeFactory>(),new(){Habilitado=true},NullLogger<CashbackPagamentoPreparacaoWorker>.Instance,_=>ticks);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await ticks.Esperando.Task.WaitAsync(TimeSpan.FromSeconds(5)); store.VerifyNoOtherCalls();
            ticks.Emitir(); await entrou.Task.WaitAsync(TimeSpan.FromSeconds(5)); ticks.Emitir();
            Assert.Equal(1,Volatile.Read(ref chamadas));
            liberar.TrySetResult(); await segundo.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1,maximo);
        }
        finally
        {
            liberar.TrySetResult();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(timeout.Token);
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(0,ativos);
    }
    [Fact]
    public async Task FalhaDoSeletorERegistradaSemSegredoENovoTickContinua()
    {
        const string segredo="MARCADOR_PRIVADO_FICTICIO";
        var falhou=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recuperou=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamadas=0;
        var store=new Mock<IJornadaFinanceiraStore>(MockBehavior.Strict);
        store.Setup(x=>x.CandidatosAsync(20,It.IsAny<CancellationToken>())).Returns((int _,CancellationToken _)=>
        {
            if(Interlocked.Increment(ref chamadas)==1)throw new InvalidOperationException(segredo);
            recuperou.TrySetResult();return Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
        });
        var logger=new Mock<ILogger<CashbackPagamentoPreparacaoWorker>>();
        logger.Setup(x=>x.Log(It.IsAny<LogLevel>(),It.IsAny<EventId>(),It.IsAny<It.IsAnyType>(),It.IsAny<Exception?>(),It.IsAny<Func<It.IsAnyType,Exception?,string>>()))
            .Callback(new InvocationAction(invocation=>
            {
                Assert.Null(invocation.Arguments[3]);
                Assert.DoesNotContain(segredo,invocation.Arguments[2].ToString());
                Assert.Contains(nameof(InvalidOperationException),invocation.Arguments[2].ToString());
                falhou.TrySetResult();
            }));
        using var services=new ServiceCollection().AddScoped(_=>store.Object).BuildServiceProvider();
        using var ticks=new Ticks();
        using var worker=new CashbackPagamentoPreparacaoWorker(services.GetRequiredService<IServiceScopeFactory>(),new(){Habilitado=true},logger.Object,_=>ticks);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            ticks.Emitir();await falhou.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1,Volatile.Read(ref chamadas));
            ticks.Emitir();await recuperou.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2,Volatile.Read(ref chamadas));
        }
        finally
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(timeout.Token);await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class Ticks:IProcessamentoPixTicks
    {
        private readonly Channel<bool> _ticks=Channel.CreateUnbounded<bool>();
        public TaskCompletionSource Esperando {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Emitir()=>_ticks.Writer.TryWrite(true);
        public async ValueTask<bool> AguardarAsync(CancellationToken ct){Esperando.TrySetResult();return await _ticks.Reader.ReadAsync(ct);}
        public void Dispose()=>_ticks.Writer.TryComplete();
    }
}

using API.Processing;
using Application.Contas;
using Infrastructure.Email;
using Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace API.Tests.Processing;

public sealed class EmailOutboxWorkerTests
{
    [Fact]
    public async Task AguardaPrimeiroTickDeduplicaEProcessaSemSobreposicao()
    {
        var id = Guid.NewGuid();
        var envio = new EnvioEmail(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "ficticio@example.invalid", TemplateEmail.Vistoria, 1, null);
        var entrou = NewSignal();
        var liberar = NewSignal();
        var terminou = NewSignal();
        var store = new Mock<IEmailOutboxStore>(MockBehavior.Strict);
        store.Setup(s => s.SelecionarAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { id, id });
        store.SetupSequence(s => s.AdquirirAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(envio).ReturnsAsync((EnvioEmail?)null);
        store.Setup(s => s.FinalizarAsync(envio, EstadoEmail.Enviado, null, It.IsAny<CancellationToken>()))
            .Callback(() => terminou.TrySetResult()).Returns(Task.CompletedTask);
        var sender = new Mock<IEmailSender>(MockBehavior.Strict);
        sender.Setup(s => s.EnviarAsync(It.IsAny<EmailTransacional>(), It.IsAny<CancellationToken>()))
            .Returns(async (EmailTransacional _, CancellationToken ct) =>
            {
                entrou.TrySetResult();
                await liberar.Task.WaitAsync(ct);
            });
        var time = new ManualTime();
        await using var services = new ServiceCollection().AddSingleton(store.Object).AddSingleton(sender.Object).BuildServiceProvider();
        using var worker = NewWorker(services, time);
        await worker.StartAsync(default);
        await time.Created.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.VerifyNoOtherCalls();
        time.Tick();
        await entrou.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Tick();
        store.Verify(s => s.SelecionarAsync(20, It.IsAny<CancellationToken>()), Times.Once);
        liberar.TrySetResult();
        await terminou.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);
        sender.Verify(s => s.EnviarAsync(It.IsAny<EmailTransacional>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FalhaDoSeletorNaoExpoeExceptionEEsperaOutroTick()
    {
        const string privado = "MARCADOR_PRIVADO_FICTICIO";
        var recovered = NewSignal();
        var logged = NewSignal();
        var store = new Mock<IEmailOutboxStore>();
        var contador = 0;
        store.Setup(s => s.SelecionarAsync(20, It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref contador) == 1)
                    throw new InvalidOperationException(privado);
                recovered.TrySetResult();
                return Task.FromResult<IReadOnlyList<Guid>>([]);
            });
        var logger = new InspectLogger((message, exception) =>
        {
            Assert.Null(exception);
            Assert.DoesNotContain(privado, message);
            logged.TrySetResult();
        });

        var time = new ManualTime();
        await using var services = new ServiceCollection().AddSingleton(store.Object).BuildServiceProvider();
        using var worker = NewWorker(services, time, logger);
        await worker.StartAsync(default);
        await time.Created.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Tick();
        await logged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref contador));
        time.Tick();
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task FalhaIndividualPermiteConcluirOProximoItem()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var primeiro = new EnvioEmail(a, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "primeiro@example.invalid", TemplateEmail.Vistoria, 1, null);
        var segundo = primeiro with { Id = b, Email = "segundo@example.invalid" };
        var terminou = NewSignal();
        var store = new Mock<IEmailOutboxStore>(MockBehavior.Strict);
        store.Setup(s => s.SelecionarAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { a, b });
        store.Setup(s => s.AdquirirAsync(a, It.IsAny<CancellationToken>())).ReturnsAsync(primeiro);
        store.Setup(s => s.AdquirirAsync(b, It.IsAny<CancellationToken>())).ReturnsAsync(segundo);
        store.Setup(s => s.FinalizarAsync(primeiro, EstadoEmail.FalhaPermanente, 1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.FinalizarAsync(segundo, EstadoEmail.Enviado, null, It.IsAny<CancellationToken>()))
            .Callback(() => terminou.TrySetResult()).Returns(Task.CompletedTask);
        var sender = new Mock<IEmailSender>(MockBehavior.Strict);
        sender.Setup(s => s.EnviarAsync(It.Is<EmailTransacional>(e => e.Destinatario == primeiro.Email), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("MARCADOR_PRIVADO"));
        sender.Setup(s => s.EnviarAsync(It.Is<EmailTransacional>(e => e.Destinatario == segundo.Email), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var time = new ManualTime();
        await using var services = new ServiceCollection().AddSingleton(store.Object).AddSingleton(sender.Object).BuildServiceProvider();
        using var worker = NewWorker(services, time);
        await worker.StartAsync(default);
        await time.Created.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Tick();
        await terminou.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);
        store.Verify(s => s.FinalizarAsync(primeiro, EstadoEmail.FalhaPermanente, 1, It.IsAny<CancellationToken>()), Times.Once);
        sender.Verify(s => s.EnviarAsync(It.IsAny<EmailTransacional>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static EmailOutboxWorker NewWorker(IServiceProvider services, TimeProvider time,
        ILogger<EmailOutboxWorker>? log = null) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), new EmailWorkerOptions(), time,
            new Uri("https://example.invalid"), log ?? NullLogger<EmailOutboxWorker>.Instance);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class InspectLogger(Action<string, Exception?> inspect) : ILogger<EmailOutboxWorker>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => inspect(formatter(state, exception), exception);
    }

    private sealed class ManualTime : TimeProvider
    {
        private Action? tick;
        public TaskCompletionSource Created { get; } = NewSignal();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            tick = () => callback(state);
            Created.TrySetResult();
            return new Timer();
        }

        public void Tick() => tick?.Invoke();

        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

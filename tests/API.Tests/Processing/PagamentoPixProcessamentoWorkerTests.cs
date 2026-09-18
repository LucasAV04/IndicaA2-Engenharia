using API.Processing;
using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Application.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace API.Tests.Processing;

public sealed class PagamentoPixProcessamentoWorkerTests
{
    [Fact]
    public async Task ExecutarCicloAsync_DeveProcessarIdsDistintosSequencialmenteEContinuarAposFalha()
    {
        var primeiro = Guid.NewGuid();
        var segundo = Guid.NewGuid();
        var seletor = new Mock<IPagamentoPixCandidatoProcessamentoStore>();
        seletor.Setup(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { primeiro, primeiro, segundo });
        var processador = new Mock<IPagamentoPixProcessamentoService>();
        processador.Setup(x => x.ProcessarAsync(primeiro, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha controlada"));
        processador.Setup(x => x.ProcessarAsync(segundo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoPagamentoPix.Criar(segundo, StatusProcessamentoPagamentoPix.Terminal));

        var services = new ServiceCollection();
        services.AddScoped(_ => seletor.Object);
        services.AddScoped(_ => processador.Object);
        using var provider = services.BuildServiceProvider();
        var worker = new PagamentoPixProcessamentoWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PagamentoPixProcessamentoWorkerOptions { Habilitado = true }),
            NullLogger<PagamentoPixProcessamentoWorker>.Instance);

        await worker.ExecutarCicloAsync(CancellationToken.None);

        processador.Verify(x => x.ProcessarAsync(primeiro, It.IsAny<CancellationToken>()), Times.Once);
        processador.Verify(x => x.ProcessarAsync(segundo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(4, 20)]
    [InlineData(30, 0)]
    public void Options_QuandoWorkerHabilitadoEValorForInvalido_DeveFalhar(int intervalo, int lote)
    {
        var options = new PagamentoPixProcessamentoWorkerOptions
        {
            Habilitado = true,
            IntervaloSegundos = intervalo,
            TamanhoLote = lote
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public async Task ExecutarCicloAsync_QuandoDesabilitado_NaoDeveCriarEscopo()
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var worker = new PagamentoPixProcessamentoWorker(
            scopes.Object,
            Options.Create(new PagamentoPixProcessamentoWorkerOptions { Habilitado = false }),
            NullLogger<PagamentoPixProcessamentoWorker>.Instance);

        await worker.ExecutarCicloAsync(CancellationToken.None);

        scopes.Verify(x => x.CreateScope(), Times.Never);
    }

    [Fact]
    public async Task ExecutarCicloAsync_QuandoExcecaoContiverSegredo_NaoDeveRegistrarMensagem()
    {
        var pagamento = Guid.NewGuid();
        var seletor = new Mock<IPagamentoPixCandidatoProcessamentoStore>();
        seletor.Setup(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pagamento });
        var processador = new Mock<IPagamentoPixProcessamentoService>();
        processador.Setup(x => x.ProcessarAsync(pagamento, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SEGREDO-FICTICIO-NAO-LOGAR"));
        var logger = new LoggerCapturador<PagamentoPixProcessamentoWorker>();
        var services = new ServiceCollection();
        services.AddScoped(_ => seletor.Object);
        services.AddScoped(_ => processador.Object);
        using var provider = services.BuildServiceProvider();
        var worker = new PagamentoPixProcessamentoWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PagamentoPixProcessamentoWorkerOptions { Habilitado = true }), logger);

        await worker.ExecutarCicloAsync(CancellationToken.None);

        Assert.DoesNotContain("SEGREDO-FICTICIO-NAO-LOGAR", logger.Texto);
        Assert.Contains(nameof(InvalidOperationException), logger.Texto);
    }

    [Fact]
    public async Task ExecutarCicloAsync_QuandoSeletorFalhar_DeveConterFalhaESanitizarLog()
    {
        var seletor = new Mock<IPagamentoPixCandidatoProcessamentoStore>();
        seletor.Setup(x => x.ObterCandidatosAsync(20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SEGREDO-DO-SELETOR"));
        var processador = new Mock<IPagamentoPixProcessamentoService>(MockBehavior.Strict);
        var logger = new LoggerCapturador<PagamentoPixProcessamentoWorker>();
        var services = new ServiceCollection();
        services.AddScoped(_ => seletor.Object);
        services.AddScoped(_ => processador.Object);
        using var provider = services.BuildServiceProvider();
        var worker = new PagamentoPixProcessamentoWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PagamentoPixProcessamentoWorkerOptions { Habilitado = true }), logger);

        await worker.ExecutarCicloAsync(CancellationToken.None);

        Assert.Contains("SelecaoDeCandidatos", logger.Texto);
        Assert.Contains(nameof(InvalidOperationException), logger.Texto);
        Assert.DoesNotContain("SEGREDO-DO-SELETOR", logger.Texto);
        processador.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(3600, 100)]
    public void Options_QuandoHabilitadoELimitesForemValidos_DeveAceitar(int intervalo, int lote)
    {
        var options = new PagamentoPixProcessamentoWorkerOptions
        {
            Habilitado = true,
            IntervaloSegundos = intervalo,
            TamanhoLote = lote
        };

        options.Validate();
    }

    private sealed class LoggerCapturador<T> : ILogger<T>
    {
        public string Texto { get; private set; } = string.Empty;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Texto += formatter(state, exception);
        }
    }
}

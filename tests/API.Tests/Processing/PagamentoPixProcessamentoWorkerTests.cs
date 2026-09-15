using API.Processing;
using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Application.Models;
using Microsoft.Extensions.DependencyInjection;
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
            Options.Create(new PagamentoPixProcessamentoWorkerOptions()),
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
}

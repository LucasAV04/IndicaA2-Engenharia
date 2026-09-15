using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Microsoft.Extensions.Options;

namespace API.Processing;

/// <summary>
/// Executa ciclos sequenciais e somente quando habilitado explicitamente.
/// Não toma decisões financeiras nem acessa o provider.
/// </summary>
public sealed class PagamentoPixProcessamentoWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PagamentoPixProcessamentoWorkerOptions> options,
    ILogger<PagamentoPixProcessamentoWorker> logger) : BackgroundService
{
    private readonly PagamentoPixProcessamentoWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        if (!_options.Habilitado)
        {
            logger.LogInformation("Worker de processamento Pix permanece desabilitado.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervaloSegundos));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ExecutarCicloAsync(stoppingToken);
    }

    public async Task ExecutarCicloAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var seletor = scope.ServiceProvider.GetRequiredService<IPagamentoPixCandidatoProcessamentoStore>();
        var processador = scope.ServiceProvider.GetRequiredService<IPagamentoPixProcessamentoService>();
        var ids = await seletor.ObterCandidatosAsync(_options.TamanhoLote, cancellationToken);

        foreach (var pagamentoPixId in ids.Distinct())
        {
            try
            {
                var resultado = await processador.ProcessarAsync(pagamentoPixId, cancellationToken);
                logger.LogInformation("Ciclo Pix concluído para {PagamentoPixId} com {Status}", pagamentoPixId, resultado.Status);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha ao processar Pagamento Pix {PagamentoPixId}", pagamentoPixId);
            }
        }
    }
}

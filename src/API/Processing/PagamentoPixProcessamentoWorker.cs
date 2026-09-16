using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Microsoft.Extensions.Options;

namespace API.Processing;

/// <summary>
/// Executa ciclos sequenciais e somente quando habilitado explicitamente.
/// Não toma decisões financeiras nem acessa o provider.
/// </summary>
public sealed class PagamentoPixProcessamentoWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<PagamentoPixProcessamentoWorker> logger;
    private readonly PagamentoPixProcessamentoWorkerOptions _options;
    private readonly Func<TimeSpan, IProcessamentoPixTicks> _criarTicks;

    public PagamentoPixProcessamentoWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PagamentoPixProcessamentoWorkerOptions> options,
        ILogger<PagamentoPixProcessamentoWorker> logger)
        : this(scopeFactory, options, logger, intervalo => new ProcessamentoPixTicks(intervalo)) { }

    internal PagamentoPixProcessamentoWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PagamentoPixProcessamentoWorkerOptions> options,
        ILogger<PagamentoPixProcessamentoWorker> logger,
        Func<TimeSpan, IProcessamentoPixTicks> criarTicks)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        _options = options.Value;
        _criarTicks = criarTicks;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        if (!_options.Habilitado)
        {
            logger.LogInformation("Worker de processamento Pix permanece desabilitado.");
            return;
        }

        using var timer = _criarTicks(TimeSpan.FromSeconds(_options.IntervaloSegundos));
        try
        {
            while (await timer.AguardarAsync(stoppingToken))
            {
                try
                {
                    await ExecutarCicloAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    RegistrarFalhaSegura("SelecaoDeCandidatos", exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task ExecutarCicloAsync(CancellationToken cancellationToken)
    {
        if (!_options.Habilitado)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        using var scope = scopeFactory.CreateScope();
        var seletor = scope.ServiceProvider.GetRequiredService<IPagamentoPixCandidatoProcessamentoStore>();
        var processador = scope.ServiceProvider.GetRequiredService<IPagamentoPixProcessamentoService>();
        IReadOnlyCollection<Guid> ids;
        try
        {
            ids = await seletor.ObterCandidatosAsync(_options.TamanhoLote, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RegistrarFalhaSegura("SelecaoDeCandidatos", exception);
            return;
        }

        foreach (var pagamentoPixId in ids.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                RegistrarFalhaSegura("ProcessamentoPagamentoPix", exception, pagamentoPixId);
            }
        }
    }

    private void RegistrarFalhaSegura(string evento, Exception exception, Guid? pagamentoPixId = null)
    {
        logger.LogError(
            "Falha segura no worker Pix: {Evento}; {TipoExcecao}; {PagamentoPixId}",
            evento,
            exception.GetType().Name,
            pagamentoPixId);
    }
}

using Application.Recebimentos;

namespace API.Processing;

public sealed class RecebimentoPixVistoriaWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RecebimentoPixOptions _options;
    private readonly ILogger<RecebimentoPixVistoriaWorker> _logger;
    private readonly Func<TimeSpan,IProcessamentoPixTicks> _ticks;
    public RecebimentoPixVistoriaWorker(IServiceScopeFactory scopes,RecebimentoPixOptions options,ILogger<RecebimentoPixVistoriaWorker> logger)
        : this(scopes,options,logger,intervalo=>new ProcessamentoPixTicks(intervalo)) { }
    internal RecebimentoPixVistoriaWorker(IServiceScopeFactory scopes,RecebimentoPixOptions options,ILogger<RecebimentoPixVistoriaWorker> logger,Func<TimeSpan,IProcessamentoPixTicks> ticks)
    { _scopes=scopes; _options=options; _logger=logger; _ticks=ticks; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!_options.Habilitado || !_options.ProcessamentoWorker.Habilitado) return;
        using var timer=_ticks(TimeSpan.FromSeconds(_options.ProcessamentoWorker.IntervaloSegundos));
        try
        {
            while(await timer.AguardarAsync(stoppingToken))
            {
                try { await CicloAsync(stoppingToken); }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { return; }
                catch(Exception ex) { Falha(ex); }
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
    }
    internal async Task CicloAsync(CancellationToken ct)
    {
        if(!_options.Habilitado || !_options.ProcessamentoWorker.Habilitado) return;
        using var scope=_scopes.CreateScope();
        var seletor=scope.ServiceProvider.GetRequiredService<IRecebimentoPixCandidatoStore>();
        var processor=scope.ServiceProvider.GetRequiredService<IRecebimentoPixProcessamentoService>();
        var limite=_options.ProcessamentoWorker.TamanhoLote;
        var eventos=(await seletor.EventosAsync(limite,ct)).Distinct().Take(limite).ToArray();
        foreach(var id in eventos) await Item(()=>processor.ProcessarEventoAsync(id,ct),ct);
        var restante=limite-eventos.Length;
        if(restante==0) return;
        foreach(var id in (await seletor.CobrançasAsync(restante,ct)).Distinct().Take(restante))
            await Item(()=>processor.ProcessarCobrancaAsync(id,ct),ct);
    }
    private async Task Item(Func<Task> action,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { await action(); }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception ex) { Falha(ex); }
    }
    private void Falha(Exception ex) => _logger.LogError("Falha segura do recebimento Pix: {Tipo}",ex.GetType().Name);
}

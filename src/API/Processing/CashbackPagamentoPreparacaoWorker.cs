using Application.Jornada;

namespace API.Processing;

public sealed class CashbackPreparacaoOptions
{
    public bool Habilitado { get; set; }
    public int IntervaloSegundos { get; set; }=60;
    public int TamanhoLote { get; set; }=20;
    public void Validar()
    {
        if(Habilitado && (IntervaloSegundos is <5 or >3600 || TamanhoLote is <1 or >100))
            throw new InvalidOperationException("Configuração inválida do worker de preparação de Cashback.");
    }
}
public sealed class CashbackPagamentoPreparacaoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly CashbackPreparacaoOptions _options;
    private readonly ILogger<CashbackPagamentoPreparacaoWorker> _logger;
    private readonly Func<TimeSpan,IProcessamentoPixTicks> _ticks;
    public CashbackPagamentoPreparacaoWorker(IServiceScopeFactory scopes,CashbackPreparacaoOptions options,ILogger<CashbackPagamentoPreparacaoWorker> logger)
        :this(scopes,options,logger,t=>new ProcessamentoPixTicks(t)) { }
    internal CashbackPagamentoPreparacaoWorker(IServiceScopeFactory scopes,CashbackPreparacaoOptions options,ILogger<CashbackPagamentoPreparacaoWorker> logger,Func<TimeSpan,IProcessamentoPixTicks> ticks)
    { _scopes=scopes; _options=options; _logger=logger; _ticks=ticks; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!_options.Habilitado) return;
        using var ticks=_ticks(TimeSpan.FromSeconds(_options.IntervaloSegundos));
        try
        {
            while(await ticks.AguardarAsync(stoppingToken))
            {
                try { await Ciclo(stoppingToken); }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { return; }
                catch(Exception e) { Falha(e); }
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
    }
    internal async Task Ciclo(CancellationToken ct)
    {
        if(!_options.Habilitado) return;
        using var scope=_scopes.CreateScope(); var store=scope.ServiceProvider.GetRequiredService<IJornadaFinanceiraStore>();
        foreach(var id in (await store.CandidatosAsync(_options.TamanhoLote,ct)).Distinct().Take(_options.TamanhoLote))
        {
            ct.ThrowIfCancellationRequested();
            try { await store.PrepararCashbackAsync(id,ct); }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
            catch(Exception e) { Falha(e); }
        }
    }
    private void Falha(Exception e)=>_logger.LogError("Falha na preparação da jornada: {Tipo}",e.GetType().Name);
}

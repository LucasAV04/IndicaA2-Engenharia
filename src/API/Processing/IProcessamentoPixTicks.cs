namespace API.Processing;

// Seam interna: produção usa PeriodicTimer; testes controlam cada tick.
internal interface IProcessamentoPixTicks : IDisposable
{
    ValueTask<bool> AguardarAsync(CancellationToken cancellationToken);
}

internal sealed class ProcessamentoPixTicks(TimeSpan intervalo) : IProcessamentoPixTicks
{
    private readonly PeriodicTimer _timer = new(intervalo);
    public ValueTask<bool> AguardarAsync(CancellationToken cancellationToken) =>
        _timer.WaitForNextTickAsync(cancellationToken);
    public void Dispose() => _timer.Dispose();
}

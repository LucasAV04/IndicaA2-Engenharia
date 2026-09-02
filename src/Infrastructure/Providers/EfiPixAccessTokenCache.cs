namespace Infrastructure.Providers;

internal sealed class EfiPixAccessTokenCache
{
    private static readonly TimeSpan MargemDeRenovacao = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, EfiPixAccessTokenEntry> _tokensPorEscopo = new(StringComparer.Ordinal);

    public EfiPixAccessTokenCache(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<string> ObterAsync(
        string scope,
        Func<CancellationToken, Task<EfiPixAccessToken>> criarTokenAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(criarTokenAsync);

        if (PossuiTokenValido(scope, out var tokenExistente))
            return tokenExistente;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (PossuiTokenValido(scope, out tokenExistente))
                return tokenExistente;

            var token = await criarTokenAsync(cancellationToken);
            _tokensPorEscopo[scope] = new EfiPixAccessTokenEntry(
                token.Value,
                _timeProvider.GetUtcNow().AddSeconds(token.ExpiresInSeconds));
            return token.Value;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool PossuiTokenValido(string scope, out string token)
    {
        if (_tokensPorEscopo.TryGetValue(scope, out var entry)
            && !string.IsNullOrWhiteSpace(entry.Value)
            && entry.ExpiresAtUtc > _timeProvider.GetUtcNow().Add(MargemDeRenovacao))
        {
            token = entry.Value;
            return true;
        }

        token = string.Empty;
        return false;
    }
}

internal sealed record EfiPixAccessToken(string Value, int ExpiresInSeconds);

internal sealed record EfiPixAccessTokenEntry(string Value, DateTimeOffset ExpiresAtUtc);

using MySqlConnector;

namespace Infrastructure.Database;

internal enum PontoTransacionalPix
{
    ClaimAntesDeInserirEnvio,
    AntesDeFinalizarAuditoriaEnvio,
    AuditoriaEnvioAntesDeLiberarLease,
    PagamentoAtualizadoAntesDoCashback,
    AuditoriasConsultaAntesDeLiberarLease
}

// Instância por store; nenhuma configuração pública ou estado global mutável.
internal delegate Task InterceptadorTransacionalPix(
    PontoTransacionalPix ponto, MySqlConnection connection,
    MySqlTransaction transaction, CancellationToken cancellationToken);

internal static class TransacaoPixSemIntercepcao
{
    internal static Task ExecutarAsync(PontoTransacionalPix ponto, MySqlConnection connection,
        MySqlTransaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;
}

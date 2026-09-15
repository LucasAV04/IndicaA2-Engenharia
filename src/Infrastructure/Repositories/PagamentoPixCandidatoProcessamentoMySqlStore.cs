using Application.Interfaces.Stores;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

/// <summary>
/// Consulta de candidatos sem efeitos colaterais. A coordenação financeira é
/// sempre feita pelos stores transacionais chamados posteriormente.
/// </summary>
public sealed class PagamentoPixCandidatoProcessamentoMySqlStore(
    MySqlConnectionFactory connectionFactory) : IPagamentoPixCandidatoProcessamentoStore
{
    public async Task<IReadOnlyCollection<Guid>> ObterCandidatosAsync(
        int limite,
        CancellationToken cancellationToken = default)
    {
        if (limite is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limite));

        const string sql = """
            SELECT id
            FROM pagamentos_pix
            WHERE status = @pendente
               OR (
                    status = @processando
                AND NOT (
                       (envio_lease_id IS NOT NULL
                        AND envio_lease_expira_em IS NOT NULL
                        AND envio_lease_expira_em > UTC_TIMESTAMP(6))
                    OR (reconciliacao_lease_id IS NOT NULL
                        AND reconciliacao_lease_expira_em IS NOT NULL
                        AND reconciliacao_lease_expira_em > UTC_TIMESTAMP(6))
                )
               )
            ORDER BY updated_at, id
            LIMIT @limite;
            """;

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.Add("@pendente", MySqlDbType.Int32).Value = (int)StatusPagamentoPix.Pendente;
        command.Parameters.Add("@processando", MySqlDbType.Int32).Value = (int)StatusPagamentoPix.Processando;
        command.Parameters.Add("@limite", MySqlDbType.Int32).Value = limite;

        var candidatos = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            candidatos.Add(reader.ObterGuid("id"));

        return candidatos.AsReadOnly();
    }
}

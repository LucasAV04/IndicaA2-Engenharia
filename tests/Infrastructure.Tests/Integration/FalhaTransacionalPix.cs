using Infrastructure.Database;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

internal sealed class FalhaTransacionalPixException : Exception { }

internal static class FalhaTransacionalPix
{
    public static InterceptadorTransacionalPix LancarEm(PontoTransacionalPix alvo) =>
        (ponto, connection, transaction, ct) => ponto == alvo
            ? throw new FalhaTransacionalPixException()
            : Task.CompletedTask;

    public static InterceptadorTransacionalPix ExpirarEm(PontoTransacionalPix alvo, Guid pagamentoId, bool envio) =>
        async (ponto, connection, transaction, ct) =>
        {
            if (ponto != alvo) return;
            var coluna = envio ? "envio_lease_expira_em" : "reconciliacao_lease_expira_em";
            await using var command = new MySqlCommand(
                $"UPDATE pagamentos_pix SET {coluna} = UTC_TIMESTAMP(6) - INTERVAL 1 SECOND WHERE id = @id",
                connection, transaction);
            command.Parameters.AddWithValue("@id", pagamentoId.ToString());
            Assert.Equal(1, await command.ExecuteNonQueryAsync(ct));
        };
}

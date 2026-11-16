using System.Data;
using Application.Relatorios;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class RelatorioOperacionalMySqlStore(MySqlConnectionFactory factory) : IRelatorioOperacionalStore
{
    public async Task<PaginaRelatorio> ConsultarAsync(FiltroRelatorio filtro, bool exportar, CancellationToken ct)
    {
        if (!filtro.Valido)
            throw new ArgumentException("Filtro inválido.", nameof(filtro));

        // Fragmentos constantes. Valores fornecidos pelo cliente sempre são parâmetros.
        var (table, date, amount, scope) = filtro.Tipo switch
        {
            TipoRelatorio.PagamentosRecebidos => ("pagamentos_vistoria", "pago_em", "valor", "status=1"),
            TipoRelatorio.CashbacksDisponiveis => ("cashbacks", "updated_at", "valor", "status=1"),
            TipoRelatorio.CashbacksPagos => ("cashbacks", "updated_at", "valor", "status=2"),
            TipoRelatorio.PixEmAberto => ("pagamentos_pix", "updated_at", "valor", "status IN (0,1)"),
            TipoRelatorio.FalhasFinanceiras => ("pagamentos_pix", "updated_at", "valor", "status IN (3,4)"),
            TipoRelatorio.CobrancasDivergentes => ("cobrancas_pix_vistoria", "updated_at", "valor", "status=10"),
            TipoRelatorio.Emails => ("email_outbox", "criado_em", "NULL", "TRUE"),
            _ => throw new ArgumentOutOfRangeException(nameof(filtro))
        };
        var where = $"WHERE {scope} AND {date} >= @de AND {date} < @ate AND (@status IS NULL OR status=@status)";
        await using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, true, ct);
        await using var aggregate = Create($"SELECT COUNT(*), SUM({amount}) FROM {table} {where}");
        long total;
        decimal? value;
        await using (var reader = await aggregate.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            total = reader.GetInt64(0);
            value = reader.IsDBNull(1) ? (amount == "NULL" ? null : 0m) : reader.GetDecimal(1);
        }

        var lines = new List<LinhaRelatorio>();
        if (exportar && total > FiltroRelatorio.LimiteExportacao)
            return new PaginaRelatorio(total, value, lines);

        await using var data = Create($"""
            SELECT id, {date}, status, {amount} FROM {table} {where}
            ORDER BY {date} DESC,id DESC LIMIT @limit OFFSET @offset
            """);
        data.Parameters.AddWithValue("@limit", exportar ? FiltroRelatorio.LimiteExportacao : filtro.Tamanho);
        data.Parameters.AddWithValue("@offset", exportar ? 0 : filtro.Deslocamento);
        await using (var reader = await data.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                lines.Add(new LinhaRelatorio(reader.ObterGuid("id"),
                    DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc), reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetDecimal(3)));
        }
        await tx.CommitAsync(ct);
        return new PaginaRelatorio(total, value, lines);

        MySqlCommand Create(string sql)
        {
            var cmd = new MySqlCommand(sql, connection, tx);
            cmd.Parameters.AddWithValue("@de", filtro.DeUtc);
            cmd.Parameters.AddWithValue("@ate", filtro.AteUtc);
            cmd.Parameters.AddWithValue("@status", (object?)filtro.Status ?? DBNull.Value);
            return cmd;
        }
    }
}

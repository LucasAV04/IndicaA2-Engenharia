using System.Data;
using Application.DTOs.Admin;
using Application.Interfaces.Stores;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

// Uma única transação somente leitura mantém os agregados no mesmo snapshot.
public sealed class AdminDashboardMySqlStore(MySqlConnectionFactory factory) : IAdminDashboardStore
{
    public async Task<DashboardResponseDto> ObterAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, isReadOnly: true, cancellationToken: cancellationToken);
        var indicacoes = await ContarAsync<StatusIndicacao>("indicacoes");
        var vistorias = await ContarAsync<StatusVistoria>("vistorias");
        var pagamentos = await ContarAsync<StatusPagamentoVistoria>("pagamentos_vistoria");
        var cashbacks = await ContarAsync<StatusCashback>("cashbacks");
        var pix = await ContarAsync<StatusPagamentoPix>("pagamentos_pix");

        // Tabelas e colunas constantes; nenhum dado de entrada é interpolado.
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM usuarios) AS total_usuarios,
                (SELECT COUNT(*) FROM usuarios WHERE status = @ativo) AS ativos,
                (SELECT COALESCE(SUM(valor), 0) FROM pagamentos_vistoria WHERE status = @confirmado) AS receita,
                (SELECT COALESCE(SUM(valor), 0) FROM pagamentos_vistoria WHERE status = @pendentePagamento) AS pendente,
                (SELECT COALESCE(SUM(valor), 0) FROM cashbacks WHERE status = @disponivel) AS cashback_disponivel,
                (SELECT COALESCE(SUM(valor), 0) FROM cashbacks WHERE status = @pago) AS cashback_pago,
                (SELECT COALESCE(SUM(valor), 0) FROM pagamentos_pix WHERE status IN (@pendentePix, @processando)) AS pix_pendente,
                (SELECT COALESCE(SUM(valor), 0) FROM pagamentos_pix WHERE status = @concluido) AS pix_concluido,
                UTC_TIMESTAMP(6) AS calculado_em
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@ativo", (int)StatusUsuario.Ativo);
        command.Parameters.AddWithValue("@confirmado", (int)StatusPagamentoVistoria.Confirmado);
        command.Parameters.AddWithValue("@pendentePagamento", (int)StatusPagamentoVistoria.Pendente);
        command.Parameters.AddWithValue("@disponivel", (int)StatusCashback.Disponivel);
        command.Parameters.AddWithValue("@pago", (int)StatusCashback.Pago);
        command.Parameters.AddWithValue("@pendentePix", (int)StatusPagamentoPix.Pendente);
        command.Parameters.AddWithValue("@processando", (int)StatusPagamentoPix.Processando);
        command.Parameters.AddWithValue("@concluido", (int)StatusPagamentoPix.Concluido);
        DashboardResponseDto result;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            result = new()
            {
                TotalUsuarios = reader.GetInt64(0), UsuariosAtivos = reader.GetInt64(1),
                ReceitaConfirmada = reader.GetDecimal(2), PagamentosPendentes = reader.GetDecimal(3),
                CashbackDisponivel = reader.GetDecimal(4), CashbackPago = reader.GetDecimal(5),
                PixPendenteProcessando = reader.GetDecimal(6), PixConcluido = reader.GetDecimal(7),
                CalculadoEmUtc = DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc),
                Indicacoes = indicacoes, Vistorias = vistorias, PagamentosVistoria = pagamentos,
                Cashbacks = cashbacks, PagamentosPix = pix,
                FalhasPix = pix[nameof(StatusPagamentoPix.Falhou)],
                FalhasDefinitivasPix = pix[nameof(StatusPagamentoPix.FalhaDefinitiva)]
            };
        }
        await transaction.CommitAsync(cancellationToken);
        return result;

        async Task<IReadOnlyDictionary<string, long>> ContarAsync<T>(string tabela) where T : struct, Enum
        {
            var contagens = Enum.GetValues<T>().ToDictionary(s => s.ToString(), _ => 0L);
            await using var cmd = new MySqlCommand($"SELECT status, COUNT(*) FROM {tabela} GROUP BY status", connection, transaction);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var status = (T)Enum.ToObject(typeof(T), reader.GetInt32(0));
                contagens[status.ToString()] = reader.GetInt64(1);
            }
            return contagens;
        }
    }
}

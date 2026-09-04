using Application.Interfaces.Stores;
using Application.Models;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

/// <summary>
/// Persiste a liquidação financeira de uma evidência conclusiva já auditada.
/// Nenhuma operação Pix é criada ou alterada nesta transação.
/// </summary>
public sealed class PagamentoPixAplicacaoResultadoMySqlStore : IPagamentoPixAplicacaoResultadoStore
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public PagamentoPixAplicacaoResultadoMySqlStore(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ResultadoPersistenciaAplicacaoPagamentoPix> AplicarAsync(
        AplicacaoResultadoPagamentoPixRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidarRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var pagamentoPix = await ObterPagamentoPixParaAtualizacaoAsync(
                connection,
                transaction,
                request.PagamentoPixId,
                cancellationToken);
            var cashback = await ObterCashbackParaAtualizacaoAsync(
                connection,
                transaction,
                request.CashbackId,
                cancellationToken);

            ValidarSnapshotsPersistidos(pagamentoPix, cashback, request);
            if (EstadoJaAplicado(pagamentoPix, cashback, request))
            {
                await transaction.CommitAsync(cancellationToken);
                return ResultadoPersistenciaAplicacaoPagamentoPix.JaAplicado;
            }

            ValidarEstadoInicial(pagamentoPix, cashback, request);
            if (await AtualizarPagamentoPixAsync(connection, transaction, request, cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "A atualização condicional do Pagamento Pix não foi aplicada e requer intervenção técnica.");
            }

            if (request.ResultadoConclusivo == ResultadoOperacaoPagamentoPix.Confirmado &&
                await AtualizarCashbackAsync(connection, transaction, request, cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "A atualização condicional do Cashback não foi aplicada e a transação foi revertida.");
            }

            await transaction.CommitAsync(cancellationToken);
            return ResultadoPersistenciaAplicacaoPagamentoPix.Aplicado;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ValidarRequest(AplicacaoResultadoPagamentoPixRequest request)
    {
        if (request.PagamentoPixId == Guid.Empty || request.CashbackId == Guid.Empty ||
            request.UsuarioBeneficiarioId == Guid.Empty || request.UsuarioIndicadorId == Guid.Empty ||
            request.Valor <= 0 || request.QuantidadeTentativas is < 1 or > 5)
        {
            throw new ArgumentException("A solicitação de aplicação financeira é inválida.", nameof(request));
        }

        var combinacaoValida = request.ResultadoConclusivo switch
        {
            ResultadoOperacaoPagamentoPix.Confirmado =>
                request.StatusPagamentoPixFinal == StatusPagamentoPix.Concluido &&
                request.StatusCashbackFinal == StatusCashback.Pago,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada =>
                (request.StatusPagamentoPixFinal is StatusPagamentoPix.Falhou or StatusPagamentoPix.FalhaDefinitiva) &&
                request.StatusCashbackFinal == StatusCashback.Disponivel,
            _ => false
        };
        if (!combinacaoValida)
        {
            throw new ArgumentException(
                "A solicitação possui uma combinação inválida entre evidência e estado financeiro final.",
                nameof(request));
        }
    }

    private static async Task<PagamentoPixPersistido> ObterPagamentoPixParaAtualizacaoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT cashback_id, usuario_beneficiario_id, valor, status, quantidade_tentativas
            FROM pagamentos_pix
            WHERE id = @id
            FOR UPDATE;
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("O Pagamento Pix não foi encontrado para aplicação financeira.");

        return new PagamentoPixPersistido(
            ObterGuid(reader, "cashback_id"),
            ObterGuid(reader, "usuario_beneficiario_id"),
            reader.GetDecimal(reader.GetOrdinal("valor")),
            ObterEnum<StatusPagamentoPix>(reader, "status"),
            reader.GetInt32(reader.GetOrdinal("quantidade_tentativas")));
    }

    private static async Task<CashbackPersistido> ObterCashbackParaAtualizacaoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid cashbackId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT usuario_indicador_id, valor, status
            FROM cashbacks
            WHERE id = @id
            FOR UPDATE;
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", cashbackId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("O Cashback não foi encontrado para aplicação financeira.");

        return new CashbackPersistido(
            ObterGuid(reader, "usuario_indicador_id"),
            reader.GetDecimal(reader.GetOrdinal("valor")),
            ObterEnum<StatusCashback>(reader, "status"));
    }

    private static void ValidarSnapshotsPersistidos(
        PagamentoPixPersistido pagamentoPix,
        CashbackPersistido cashback,
        AplicacaoResultadoPagamentoPixRequest request)
    {
        if (pagamentoPix.CashbackId != request.CashbackId ||
            pagamentoPix.UsuarioBeneficiarioId != request.UsuarioBeneficiarioId ||
            cashback.UsuarioIndicadorId != request.UsuarioIndicadorId ||
            pagamentoPix.Valor != request.Valor ||
            cashback.Valor != request.Valor)
        {
            throw new InvalidOperationException(
                "Os snapshots persistidos de Pagamento Pix e Cashback são incompatíveis e requerem intervenção técnica.");
        }
    }

    private static bool EstadoJaAplicado(
        PagamentoPixPersistido pagamentoPix,
        CashbackPersistido cashback,
        AplicacaoResultadoPagamentoPixRequest request) =>
        pagamentoPix.Status == request.StatusPagamentoPixFinal &&
        pagamentoPix.QuantidadeTentativas == request.QuantidadeTentativas &&
        cashback.Status == request.StatusCashbackFinal;

    private static void ValidarEstadoInicial(
        PagamentoPixPersistido pagamentoPix,
        CashbackPersistido cashback,
        AplicacaoResultadoPagamentoPixRequest request)
    {
        if (pagamentoPix.Status != StatusPagamentoPix.Processando ||
            pagamentoPix.QuantidadeTentativas != request.QuantidadeTentativas ||
            cashback.Status != StatusCashback.Disponivel)
        {
            throw new InvalidOperationException(
                "O estado persistido é incompatível com a aplicação segura do resultado financeiro.");
        }
    }

    private static async Task<int> AtualizarPagamentoPixAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        AplicacaoResultadoPagamentoPixRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pagamentos_pix
            SET status = @statusFinal, updated_at = @updatedAt
            WHERE id = @id
              AND status = @statusProcessando
              AND quantidade_tentativas = @quantidadeTentativas;
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", request.PagamentoPixId);
        command.Parameters.Add("@statusFinal", MySqlDbType.Int32).Value = (int)request.StatusPagamentoPixFinal;
        command.Parameters.Add("@statusProcessando", MySqlDbType.Int32).Value = (int)StatusPagamentoPix.Processando;
        command.Parameters.Add("@quantidadeTentativas", MySqlDbType.Int32).Value = request.QuantidadeTentativas;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = request.PagamentoPixUpdatedAt;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> AtualizarCashbackAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        AplicacaoResultadoPagamentoPixRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE cashbacks
            SET status = @statusFinal, updated_at = @updatedAt
            WHERE id = @id
              AND status = @statusDisponivel;
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", request.CashbackId);
        command.Parameters.Add("@statusFinal", MySqlDbType.Int32).Value = (int)request.StatusCashbackFinal;
        command.Parameters.Add("@statusDisponivel", MySqlDbType.Int32).Value = (int)StatusCashback.Disponivel;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = request.CashbackUpdatedAt;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Guid ObterGuid(MySqlDataReader reader, string coluna) =>
        Guid.TryParse(reader.GetString(reader.GetOrdinal(coluna)), out var valor) && valor != Guid.Empty
            ? valor
            : throw new InvalidOperationException("O identificador financeiro persistido é inválido.");

    private static TEnum ObterEnum<TEnum>(MySqlDataReader reader, string coluna)
        where TEnum : struct, Enum
    {
        var valor = reader.GetInt32(reader.GetOrdinal(coluna));
        return Enum.IsDefined(typeof(TEnum), valor)
            ? (TEnum)Enum.ToObject(typeof(TEnum), valor)
            : throw new InvalidOperationException("O status financeiro persistido é inválido.");
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private sealed record PagamentoPixPersistido(
        Guid CashbackId,
        Guid UsuarioBeneficiarioId,
        decimal Valor,
        StatusPagamentoPix Status,
        int QuantidadeTentativas);

    private sealed record CashbackPersistido(
        Guid UsuarioIndicadorId,
        decimal Valor,
        StatusCashback Status);
}

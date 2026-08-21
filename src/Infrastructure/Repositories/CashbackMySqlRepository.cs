using System.Data;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class CashbackMySqlRepository : ICashbackRepository
{
    private const string ConstraintPagamentoVistoriaPorCashback = "uq_cashbacks_pagamento_vistoria_id";

    private const string Colunas = """
        id,
        indicacao_id,
        pagamento_vistoria_id,
        usuario_indicador_id,
        valor_total_pago,
        percentual,
        valor,
        status,
        created_at,
        updated_at
        """;

    private readonly MySqlConnectionFactory _connectionFactory;

    public CashbackMySqlRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Cashback?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM cashbacks WHERE id = @id;";
        return await ObterUnicoAsync(sql, "@id", id, cancellationToken);
    }

    public async Task<Cashback?> ObterPorPagamentoVistoriaIdAsync(
        Guid pagamentoVistoriaId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM cashbacks WHERE pagamento_vistoria_id = @pagamentoVistoriaId;";
        return await ObterUnicoAsync(sql, "@pagamentoVistoriaId", pagamentoVistoriaId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Cashback>> ObterPorUsuarioIndicadorIdAsync(
        Guid usuarioIndicadorId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM cashbacks WHERE usuario_indicador_id = @usuarioIndicadorId ORDER BY created_at, id;";
        return await ObterColecaoAsync(sql, "@usuarioIndicadorId", usuarioIndicadorId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Cashback>> ObterTodosAsync(CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM cashbacks ORDER BY created_at, id;";
        return await ObterColecaoAsync(sql, null, null, cancellationToken);
    }

    public async Task AdicionarAsync(Cashback cashback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cashback);

        const string sql = """
            INSERT INTO cashbacks (
                id, indicacao_id, pagamento_vistoria_id, usuario_indicador_id,
                valor_total_pago, percentual, valor, status, created_at, updated_at)
            VALUES (
                @id, @indicacaoId, @pagamentoVistoriaId, @usuarioIndicadorId,
                @valorTotalPago, @percentual, @valor, @status, @createdAt, @updatedAt);
            """;

        try
        {
            await ExecutarComandoAsync(sql, command => AdicionarParametrosEstado(command, cashback), cancellationToken);
        }
        catch (MySqlException exception) when (EhViolacaoDePagamentoVistoriaDuplicado(exception))
        {
            throw new CashbackJaExisteException();
        }
    }

    public async Task AtualizarAsync(Cashback cashback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cashback);

        const string sql = """
            UPDATE cashbacks
            SET status = @status, updated_at = @updatedAt
            WHERE id = @id;
            """;

        await ExecutarComandoAsync(sql, command =>
        {
            AdicionarGuid(command, "@id", cashback.Id);
            command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)cashback.Status;
            command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = cashback.UpdatedAt;
        }, cancellationToken);
    }

    private async Task<Cashback?> ObterUnicoAsync(
        string sql,
        string nomeParametro,
        Guid valorParametro,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        AdicionarGuid(command, nomeParametro, valorParametro);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Materializar(reader) : null;
    }

    private async Task<IReadOnlyCollection<Cashback>> ObterColecaoAsync(
        string sql,
        string? nomeParametro,
        Guid? valorParametro,
        CancellationToken cancellationToken)
    {
        var cashbacks = new List<Cashback>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);

        if (nomeParametro is not null && valorParametro.HasValue)
            AdicionarGuid(command, nomeParametro, valorParametro.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            cashbacks.Add(Materializar(reader));

        return cashbacks.AsReadOnly();
    }

    private async Task ExecutarComandoAsync(string sql, Action<MySqlCommand> adicionarParametros, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        adicionarParametros(command);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MySqlCommand CriarComando(MySqlConnection connection, string sql) => new(sql, connection);

    private static bool EhViolacaoDePagamentoVistoriaDuplicado(MySqlException exception) =>
        exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry &&
        exception.Message.Contains(ConstraintPagamentoVistoriaPorCashback, StringComparison.OrdinalIgnoreCase);

    private static void AdicionarParametrosEstado(MySqlCommand command, Cashback cashback)
    {
        AdicionarGuid(command, "@id", cashback.Id);
        AdicionarGuid(command, "@indicacaoId", cashback.IndicacaoId);
        AdicionarGuid(command, "@pagamentoVistoriaId", cashback.PagamentoVistoriaId);
        AdicionarGuid(command, "@usuarioIndicadorId", cashback.UsuarioIndicadorId);
        command.Parameters.Add("@valorTotalPago", MySqlDbType.Decimal).Value = cashback.ValorTotalPago;
        command.Parameters.Add("@percentual", MySqlDbType.Decimal).Value = cashback.Percentual;
        command.Parameters.Add("@valor", MySqlDbType.Decimal).Value = cashback.Valor;
        command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)cashback.Status;
        command.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = cashback.CreatedAt;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = cashback.UpdatedAt;
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private static Cashback Materializar(MySqlDataReader reader)
    {
        var statusPersistido = reader.GetInt32(reader.GetOrdinal("status"));
        if (!Enum.IsDefined(typeof(StatusCashback), statusPersistido))
            throw new DataException($"O status persistido '{statusPersistido}' é inválido.");

        return Cashback.Reidratar(
            reader.ObterGuid("id"),
            reader.ObterGuid("indicacao_id"),
            reader.ObterGuid("pagamento_vistoria_id"),
            reader.ObterGuid("usuario_indicador_id"),
            reader.GetDecimal(reader.GetOrdinal("valor_total_pago")),
            reader.GetDecimal(reader.GetOrdinal("percentual")),
            reader.GetDecimal(reader.GetOrdinal("valor")),
            (StatusCashback)statusPersistido,
            ObterDataUtc(reader, "created_at"),
            ObterDataUtc(reader, "updated_at"));
    }

    private static DateTime ObterDataUtc(MySqlDataReader reader, string nomeColuna) =>
        DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal(nomeColuna)), DateTimeKind.Utc);
}

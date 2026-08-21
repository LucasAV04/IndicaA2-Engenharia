using System.Data;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class PagamentoVistoriaMySqlRepository : IPagamentoVistoriaRepository
{
    private const string ConstraintPagamentoVistoriaPorVistoria = "uq_pagamentos_vistoria_vistoria_id";

    private const string Colunas = """
        id,
        vistoria_id,
        valor,
        status,
        pago_em,
        created_at,
        updated_at
        """;

    private readonly MySqlConnectionFactory _connectionFactory;

    public PagamentoVistoriaMySqlRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagamentoVistoria?> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM pagamentos_vistoria WHERE id = @id;";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        AdicionarGuid(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? Materializar(reader)
            : null;
    }

    public async Task<PagamentoVistoria?> ObterPorVistoriaIdAsync(
        Guid vistoriaId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM pagamentos_vistoria WHERE vistoria_id = @vistoriaId;";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        AdicionarGuid(command, "@vistoriaId", vistoriaId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? Materializar(reader)
            : null;
    }

    public async Task<IReadOnlyCollection<PagamentoVistoria>> ObterTodosAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM pagamentos_vistoria ORDER BY created_at, id;";
        var pagamentosVistoria = new List<PagamentoVistoria>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            pagamentosVistoria.Add(Materializar(reader));
        }

        return pagamentosVistoria.AsReadOnly();
    }

    public async Task AdicionarAsync(
        PagamentoVistoria pagamentoVistoria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pagamentoVistoria);

        const string sql = """
            INSERT INTO pagamentos_vistoria (
                id,
                vistoria_id,
                valor,
                status,
                pago_em,
                created_at,
                updated_at)
            VALUES (
                @id,
                @vistoriaId,
                @valor,
                @status,
                @pagoEm,
                @createdAt,
                @updatedAt);
            """;

        try
        {
            await ExecutarComandoAsync(
                sql,
                command => AdicionarParametrosEstado(command, pagamentoVistoria),
                cancellationToken);
        }
        catch (MySqlException exception) when (EhViolacaoDePagamentoVistoriaDuplicado(exception))
        {
            throw new PagamentoVistoriaDuplicadoException();
        }
    }

    public async Task AtualizarAsync(
        PagamentoVistoria pagamentoVistoria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pagamentoVistoria);

        const string sql = """
            UPDATE pagamentos_vistoria
            SET
                status = @status,
                pago_em = @pagoEm,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        await ExecutarComandoAsync(sql, command =>
        {
            AdicionarGuid(command, "@id", pagamentoVistoria.Id);
            command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)pagamentoVistoria.Status;
            AdicionarDataOpcional(command, "@pagoEm", pagamentoVistoria.PagoEm);
            command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = pagamentoVistoria.UpdatedAt;
        }, cancellationToken);
    }

    private async Task ExecutarComandoAsync(
        string sql,
        Action<MySqlCommand> adicionarParametros,
        CancellationToken cancellationToken)
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
        exception.Message.Contains(ConstraintPagamentoVistoriaPorVistoria, StringComparison.OrdinalIgnoreCase);

    private static void AdicionarParametrosEstado(MySqlCommand command, PagamentoVistoria pagamentoVistoria)
    {
        AdicionarGuid(command, "@id", pagamentoVistoria.Id);
        AdicionarGuid(command, "@vistoriaId", pagamentoVistoria.VistoriaId);
        command.Parameters.Add("@valor", MySqlDbType.Decimal).Value = pagamentoVistoria.Valor;
        command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)pagamentoVistoria.Status;
        AdicionarDataOpcional(command, "@pagoEm", pagamentoVistoria.PagoEm);
        command.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = pagamentoVistoria.CreatedAt;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = pagamentoVistoria.UpdatedAt;
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private static void AdicionarDataOpcional(MySqlCommand command, string nome, DateTime? valor) =>
        command.Parameters.Add(nome, MySqlDbType.DateTime).Value = (object?)valor ?? DBNull.Value;

    private static PagamentoVistoria Materializar(MySqlDataReader reader)
    {
        var statusPersistido = reader.GetInt32(reader.GetOrdinal("status"));
        if (!Enum.IsDefined(typeof(StatusPagamentoVistoria), statusPersistido))
            throw new DataException($"O status persistido '{statusPersistido}' é inválido.");

        return PagamentoVistoria.Reidratar(
            reader.ObterGuid("id"),
            reader.ObterGuid("vistoria_id"),
            reader.GetDecimal(reader.GetOrdinal("valor")),
            (StatusPagamentoVistoria)statusPersistido,
            ObterDataOpcionalUtc(reader, "pago_em"),
            ObterDataUtc(reader, "created_at"),
            ObterDataUtc(reader, "updated_at"));
    }

    private static DateTime? ObterDataOpcionalUtc(MySqlDataReader reader, string nomeColuna)
    {
        var ordinal = reader.GetOrdinal(nomeColuna);
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }

    private static DateTime ObterDataUtc(MySqlDataReader reader, string nomeColuna) =>
        DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal(nomeColuna)), DateTimeKind.Utc);
}

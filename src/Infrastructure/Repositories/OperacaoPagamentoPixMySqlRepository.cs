using System.Data;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class OperacaoPagamentoPixMySqlRepository : IOperacaoPagamentoPixRepository
{
    private const string Colunas = """
        id, pagamento_pix_id, tipo_operacao, numero_tentativa_envio,
        referencia_idempotente, resultado, identificador_provider, codigo,
        started_at, finished_at, updated_at
        """;

    private readonly MySqlConnectionFactory _connectionFactory;

    public OperacaoPagamentoPixMySqlRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task AdicionarAsync(OperacaoPagamentoPix operacao, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operacao);
        if (operacao.FinishedAt.HasValue || operacao.Resultado.HasValue)
            throw new ArgumentException("Somente uma operação iniciada pode ser inserida na auditoria.", nameof(operacao));

        const string sql = """
            INSERT INTO operacoes_pagamento_pix (
                id, pagamento_pix_id, tipo_operacao, numero_tentativa_envio,
                referencia_idempotente, resultado, identificador_provider, codigo,
                started_at, finished_at, updated_at)
            VALUES (
                @id, @pagamentoPixId, @tipoOperacao, @numeroTentativaEnvio,
                @referenciaIdempotente, NULL, NULL, NULL,
                @startedAt, NULL, @updatedAt);
            """;

        await ExecutarAsync(sql, command =>
        {
            AdicionarGuid(command, "@id", operacao.Id);
            AdicionarGuid(command, "@pagamentoPixId", operacao.PagamentoPixId);
            command.Parameters.Add("@tipoOperacao", MySqlDbType.Int32).Value = (int)operacao.TipoOperacao;
            command.Parameters.Add("@numeroTentativaEnvio", MySqlDbType.Int32).Value =
                operacao.NumeroTentativaEnvio ?? (object)DBNull.Value;
            command.Parameters.Add("@referenciaIdempotente", MySqlDbType.VarChar).Value = operacao.ReferenciaIdempotente;
            command.Parameters.Add("@startedAt", MySqlDbType.DateTime).Value = operacao.CreatedAt;
            command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = operacao.UpdatedAt;
        }, cancellationToken);
    }

    public async Task<bool> FinalizarAsync(OperacaoPagamentoPix operacao, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operacao);
        if (!operacao.FinishedAt.HasValue || !operacao.Resultado.HasValue)
            throw new ArgumentException("A operação precisa estar finalizada antes de ser persistida.", nameof(operacao));

        const string sql = """
            UPDATE operacoes_pagamento_pix
            SET
                resultado = @resultado,
                identificador_provider = @identificadorProvider,
                codigo = @codigo,
                finished_at = @finishedAt,
                updated_at = @updatedAt
            WHERE id = @id
              AND finished_at IS NULL;
            """;

        return await ExecutarAsync(sql, command =>
        {
            AdicionarGuid(command, "@id", operacao.Id);
            command.Parameters.Add("@resultado", MySqlDbType.Int32).Value = (int)operacao.Resultado.Value;
            command.Parameters.Add("@identificadorProvider", MySqlDbType.VarChar).Value =
                operacao.IdentificadorProvider ?? (object)DBNull.Value;
            command.Parameters.Add("@codigo", MySqlDbType.VarChar).Value = operacao.Codigo ?? (object)DBNull.Value;
            command.Parameters.Add("@finishedAt", MySqlDbType.DateTime).Value = operacao.FinishedAt.Value;
            command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = operacao.UpdatedAt;
        }, cancellationToken) == 1;
    }

    public Task<OperacaoPagamentoPix?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        ObterUnicaAsync($"SELECT {Colunas} FROM operacoes_pagamento_pix WHERE id = @id;", "@id", id, cancellationToken);

    public Task<IReadOnlyCollection<OperacaoPagamentoPix>> ObterPorPagamentoPixIdAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default) =>
        ObterColecaoAsync(
            $"SELECT {Colunas} FROM operacoes_pagamento_pix WHERE pagamento_pix_id = @id ORDER BY started_at, id;",
            "@id",
            pagamentoPixId,
            cancellationToken);

    public Task<IReadOnlyCollection<OperacaoPagamentoPix>> ObterAbertasAsync(CancellationToken cancellationToken = default) =>
        ObterColecaoAsync(
            $"SELECT {Colunas} FROM operacoes_pagamento_pix WHERE finished_at IS NULL ORDER BY started_at, id;",
            null,
            null,
            cancellationToken);

    private async Task<OperacaoPagamentoPix?> ObterUnicaAsync(
        string sql,
        string parametro,
        Guid valor,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        AdicionarGuid(command, parametro, valor);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Materializar(reader) : null;
    }

    private async Task<IReadOnlyCollection<OperacaoPagamentoPix>> ObterColecaoAsync(
        string sql,
        string? parametro,
        Guid? valor,
        CancellationToken cancellationToken)
    {
        var operacoes = new List<OperacaoPagamentoPix>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        if (parametro is not null && valor.HasValue)
            AdicionarGuid(command, parametro, valor.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            operacoes.Add(Materializar(reader));
        return operacoes.AsReadOnly();
    }

    private async Task<int> ExecutarAsync(
        string sql,
        Action<MySqlCommand> adicionarParametros,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        adicionarParametros(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private static OperacaoPagamentoPix Materializar(MySqlDataReader reader)
    {
        var resultadoOrdinal = reader.GetOrdinal("resultado");
        var finishedAtOrdinal = reader.GetOrdinal("finished_at");
        return OperacaoPagamentoPix.Reidratar(
            reader.ObterGuid("id"),
            reader.ObterGuid("pagamento_pix_id"),
            (TipoOperacaoPagamentoPix)reader.GetInt32(reader.GetOrdinal("tipo_operacao")),
            reader.IsDBNull(reader.GetOrdinal("numero_tentativa_envio"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("numero_tentativa_envio")),
            reader.GetString(reader.GetOrdinal("referencia_idempotente")),
            reader.IsDBNull(resultadoOrdinal) ? null : (ResultadoOperacaoPagamentoPix)reader.GetInt32(resultadoOrdinal),
            ObterOpcional(reader, "identificador_provider"),
            ObterOpcional(reader, "codigo"),
            EmUtc(reader.GetDateTime(reader.GetOrdinal("started_at"))),
            EmUtc(reader.GetDateTime(reader.GetOrdinal("updated_at"))),
            reader.IsDBNull(finishedAtOrdinal) ? null : EmUtc(reader.GetDateTime(finishedAtOrdinal)));
    }

    private static string? ObterOpcional(MySqlDataReader reader, string coluna) =>
        reader.IsDBNull(reader.GetOrdinal(coluna)) ? null : reader.GetString(reader.GetOrdinal(coluna));

    private static DateTime EmUtc(DateTime data) => DateTime.SpecifyKind(data, DateTimeKind.Utc);
}

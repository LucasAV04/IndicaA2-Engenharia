using Application.Interfaces.Stores;
using Application.Models;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

/// <summary>
/// Cria a auditoria de consulta somente depois de coordenar a ordem no MySQL.
/// A transação nunca engloba a chamada HTTP ao provider.
/// </summary>
public sealed class PagamentoPixReconciliacaoMySqlStore : IPagamentoPixReconciliacaoStore
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public PagamentoPixReconciliacaoMySqlStore(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PreparacaoReconciliacaoPagamentoPixResult> PrepararConsultaAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pagamentoPixId, Guid.Empty);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var pagamentoPix = await ObterPagamentoPixParaCoordenacaoAsync(
                connection, transaction, pagamentoPixId, cancellationToken);
            if (pagamentoPix.Status != StatusPagamentoPix.Processando)
            {
                await transaction.CommitAsync(cancellationToken);
                return PreparacaoReconciliacaoPagamentoPixResult.NaoAplicavel();
            }

            var operacoes = await ObterOperacoesParaCoordenacaoAsync(
                connection, transaction, pagamentoPixId, cancellationToken);
            var cicloAtual = IdentificarCicloAtual(operacoes, pagamentoPix.QuantidadeTentativas);
            var resultadoConclusivo = ObterResultadoConclusivo(cicloAtual);

            if (cicloAtual.Consultas.Any(operacao => !operacao.FinishedAt.HasValue))
            {
                await transaction.CommitAsync(cancellationToken);
                return PreparacaoReconciliacaoPagamentoPixResult.ConsultaEmAndamento();
            }

            if (resultadoConclusivo.HasValue)
            {
                var envioResolvido = false;
                if (!cicloAtual.Envio.FinishedAt.HasValue)
                {
                    envioResolvido = await FinalizarEnvioAbertoAsync(
                        connection,
                        transaction,
                        cicloAtual.Envio.Id,
                        resultadoConclusivo.Value,
                        cancellationToken);
                    if (!envioResolvido)
                    {
                        throw new InvalidOperationException(
                            "A finalização do envio aberto não pôde ser coordenada e requer intervenção técnica.");
                    }
                }

                await transaction.CommitAsync(cancellationToken);
                return PreparacaoReconciliacaoPagamentoPixResult.JaConclusivo(
                    resultadoConclusivo.Value,
                    envioResolvido);
            }

            var consulta = OperacaoPagamentoPix.IniciarConsulta(pagamentoPixId);
            await AdicionarConsultaAsync(connection, transaction, consulta, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(consulta.Id);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<PagamentoPixCoordenado> ObterPagamentoPixParaCoordenacaoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT status, quantidade_tentativas
            FROM pagamentos_pix
            WHERE id = @id
            FOR UPDATE;
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("O Pagamento Pix não foi encontrado para reconciliação.");

        return new PagamentoPixCoordenado(
            ObterEnum<StatusPagamentoPix>(reader, "status"),
            reader.GetInt32(reader.GetOrdinal("quantidade_tentativas")));
    }

    private static async Task<IReadOnlyCollection<OperacaoCoordenada>> ObterOperacoesParaCoordenacaoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tipo_operacao, numero_tentativa_envio, resultado, started_at, finished_at
            FROM operacoes_pagamento_pix
            WHERE pagamento_pix_id = @pagamentoPixId
            ORDER BY started_at, id
            FOR UPDATE;
            """;

        var operacoes = new List<OperacaoCoordenada>();
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@pagamentoPixId", pagamentoPixId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tentativaOrdinal = reader.GetOrdinal("numero_tentativa_envio");
            var resultadoOrdinal = reader.GetOrdinal("resultado");
            var finalizadaOrdinal = reader.GetOrdinal("finished_at");
            operacoes.Add(new OperacaoCoordenada(
                ObterGuid(reader, "id"),
                ObterEnum<TipoOperacaoPagamentoPix>(reader, "tipo_operacao"),
                reader.IsDBNull(tentativaOrdinal) ? null : reader.GetInt32(tentativaOrdinal),
                reader.IsDBNull(resultadoOrdinal) ? null : ObterEnum<ResultadoOperacaoPagamentoPix>(reader, "resultado"),
                EmUtc(reader.GetDateTime(reader.GetOrdinal("started_at"))),
                reader.IsDBNull(finalizadaOrdinal) ? null : EmUtc(reader.GetDateTime(finalizadaOrdinal))));
        }

        return operacoes.AsReadOnly();
    }

    private static CicloAtual IdentificarCicloAtual(
        IReadOnlyCollection<OperacaoCoordenada> historico,
        int tentativaAtual)
    {
        var envios = historico.Where(operacao =>
            operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio &&
            operacao.NumeroTentativaEnvio == tentativaAtual).ToArray();
        if (envios.Length != 1)
            throw new InvalidOperationException("Pagamento Pix deve possuir exatamente um envio para a tentativa atual.");

        if (historico.Any(operacao =>
                operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio &&
                operacao.NumeroTentativaEnvio < tentativaAtual &&
                !operacao.FinishedAt.HasValue))
        {
            throw new InvalidOperationException(
                "Pagamento Pix possui envio aberto de tentativa anterior e requer intervenção técnica.");
        }

        var envioAtual = envios[0];
        return new CicloAtual(
            envioAtual,
            historico.Where(operacao =>
                operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta &&
                operacao.CreatedAt > envioAtual.CreatedAt).ToArray());
    }

    private static ResultadoOperacaoPagamentoPix? ObterResultadoConclusivo(CicloAtual cicloAtual)
    {
        var resultados = new[] { cicloAtual.Envio }
            .Concat(cicloAtual.Consultas)
            .Where(operacao => EhConclusivo(operacao.Resultado))
            .Select(operacao => operacao.Resultado!.Value)
            .Distinct()
            .ToArray();
        if (resultados.Length > 1)
        {
            throw new InvalidOperationException(
                "Pagamento Pix possui evidências conclusivas conflitantes no ciclo da tentativa atual.");
        }

        return resultados.Length == 0 ? null : resultados[0];
    }

    private static async Task<bool> FinalizarEnvioAbertoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid operacaoId,
        ResultadoOperacaoPagamentoPix resultado,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE operacoes_pagamento_pix
            SET resultado = @resultado,
                finished_at = @finishedAt,
                updated_at = @updatedAt
            WHERE id = @id
              AND finished_at IS NULL;
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", operacaoId);
        command.Parameters.Add("@resultado", MySqlDbType.Int32).Value = (int)resultado;
        command.Parameters.Add("@finishedAt", MySqlDbType.DateTime).Value = DateTime.UtcNow;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = DateTime.UtcNow;
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task AdicionarConsultaAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        OperacaoPagamentoPix consulta,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operacoes_pagamento_pix (
                id, pagamento_pix_id, tipo_operacao, numero_tentativa_envio,
                referencia_idempotente, resultado, identificador_provider, codigo,
                started_at, finished_at, updated_at)
            VALUES (
                @id, @pagamentoPixId, @tipoOperacao, NULL,
                @referenciaIdempotente, NULL, NULL, NULL,
                @startedAt, NULL, @updatedAt);
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", consulta.Id);
        AdicionarGuid(command, "@pagamentoPixId", consulta.PagamentoPixId);
        command.Parameters.Add("@tipoOperacao", MySqlDbType.Int32).Value = (int)consulta.TipoOperacao;
        command.Parameters.Add("@referenciaIdempotente", MySqlDbType.VarChar).Value = consulta.ReferenciaIdempotente;
        command.Parameters.Add("@startedAt", MySqlDbType.DateTime).Value = consulta.CreatedAt;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = consulta.UpdatedAt;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool EhConclusivo(ResultadoOperacaoPagamentoPix? resultado) =>
        resultado is ResultadoOperacaoPagamentoPix.Confirmado or ResultadoOperacaoPagamentoPix.FalhaConfirmada;

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

    private static DateTime EmUtc(DateTime data) => DateTime.SpecifyKind(data, DateTimeKind.Utc);

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private sealed record PagamentoPixCoordenado(StatusPagamentoPix Status, int QuantidadeTentativas);

    private sealed record OperacaoCoordenada(
        Guid Id,
        TipoOperacaoPagamentoPix TipoOperacao,
        int? NumeroTentativaEnvio,
        ResultadoOperacaoPagamentoPix? Resultado,
        DateTime CreatedAt,
        DateTime? FinishedAt);

    private sealed record CicloAtual(OperacaoCoordenada Envio, IReadOnlyCollection<OperacaoCoordenada> Consultas);
}

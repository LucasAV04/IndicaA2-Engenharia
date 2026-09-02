using Application.Interfaces.Stores;
using Application.Models;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

/// <summary>
/// Executa, na mesma transação MySQL, o claim da ordem e a criação da auditoria
/// de envio. A chamada ao provider ocorre somente após o commit.
/// </summary>
public sealed class PagamentoPixEnvioMySqlStore : IPagamentoPixEnvioStore
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public PagamentoPixEnvioMySqlStore(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PreparacaoEnvioPagamentoPixResult> TentarPrepararEnvioAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pagamentoPixId, Guid.Empty);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await TentarAdquirirAsync(connection, transaction, pagamentoPixId, cancellationToken))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PreparacaoEnvioPagamentoPixResult.NaoAdquirido();
            }

            var tentativa = await ObterTentativaAsync(
                connection,
                transaction,
                pagamentoPixId,
                cancellationToken);
            var operacao = OperacaoPagamentoPix.IniciarEnvio(pagamentoPixId, tentativa);

            await AdicionarOperacaoAsync(connection, transaction, operacao, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return PreparacaoEnvioPagamentoPixResult.AdquiridoCom(operacao.Id, tentativa);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<bool> TentarAdquirirAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        var statusElegiveis = PagamentoPix.StatusElegiveisParaIniciarTentativa;
        var parametrosDeStatus = string.Join(
            ", ",
            statusElegiveis.Select((_, indice) => $"@statusElegivel{indice}"));
        var sql = $"""
            UPDATE pagamentos_pix
            SET
                status = @statusProcessando,
                quantidade_tentativas = quantidade_tentativas + 1,
                updated_at = @updatedAt
            WHERE id = @id
              AND status IN ({parametrosDeStatus})
              AND quantidade_tentativas < @tentativasMaximas;
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        command.Parameters.Add("@statusProcessando", MySqlDbType.Int32).Value = (int)StatusPagamentoPix.Processando;
        command.Parameters.Add("@tentativasMaximas", MySqlDbType.Int32).Value = PagamentoPix.TentativasMaximas;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = DateTime.UtcNow;

        foreach (var (status, indice) in statusElegiveis.Select((status, indice) => (status, indice)))
            command.Parameters.Add($"@statusElegivel{indice}", MySqlDbType.Int32).Value = (int)status;

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<int> ObterTentativaAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT quantidade_tentativas FROM pagamentos_pix WHERE id = @id;";
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        var resultado = await command.ExecuteScalarAsync(cancellationToken);

        return resultado is int tentativa && tentativa is > 0 and <= PagamentoPix.TentativasMaximas
            ? tentativa
            : throw new InvalidOperationException("A tentativa preparada do Pagamento Pix é inválida.");
    }

    private static async Task AdicionarOperacaoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        OperacaoPagamentoPix operacao,
        CancellationToken cancellationToken)
    {
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

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", operacao.Id);
        AdicionarGuid(command, "@pagamentoPixId", operacao.PagamentoPixId);
        command.Parameters.Add("@tipoOperacao", MySqlDbType.Int32).Value = (int)operacao.TipoOperacao;
        command.Parameters.Add("@numeroTentativaEnvio", MySqlDbType.Int32).Value = operacao.NumeroTentativaEnvio!.Value;
        command.Parameters.Add("@referenciaIdempotente", MySqlDbType.VarChar).Value = operacao.ReferenciaIdempotente;
        command.Parameters.Add("@startedAt", MySqlDbType.DateTime).Value = operacao.CreatedAt;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = operacao.UpdatedAt;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();
}

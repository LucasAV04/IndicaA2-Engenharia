using Application.Interfaces.Stores;
using Application.Models;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

/// <summary>
/// Decide e persiste a liquidação financeira sob bloqueio da mesma ordem usada
/// para preparar uma reconciliação. A evidência nunca é confiada a uma leitura
/// anterior à transação.
/// </summary>
public sealed class PagamentoPixAplicacaoResultadoMySqlStore : IPagamentoPixAplicacaoResultadoStore
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public PagamentoPixAplicacaoResultadoMySqlStore(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ResultadoPersistenciaAplicacaoPagamentoPix> AplicarAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pagamentoPixId, Guid.Empty);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var pagamentoPix = await ObterPagamentoPixParaAtualizacaoAsync(
                connection, transaction, pagamentoPixId, cancellationToken);
            var operacoes = await ObterOperacoesParaAtualizacaoAsync(
                connection, transaction, pagamentoPixId, cancellationToken);
            var cicloAtual = IdentificarCicloAtual(operacoes, pagamentoPix.QuantidadeTentativas);
            var resultadoConclusivo = ObterResultadoConclusivo(cicloAtual);

            if (cicloAtual.Consultas.Any(operacao => !operacao.FinishedAt.HasValue) ||
                !cicloAtual.Envio.FinishedAt.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return ResultadoPersistenciaAplicacaoPagamentoPix.RequerReconciliacao();
            }

            if (!resultadoConclusivo.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return ResultadoPersistenciaAplicacaoPagamentoPix.SemResultadoConclusivo();
            }

            var cashback = await ObterCashbackParaAtualizacaoAsync(
                connection, transaction, pagamentoPix.CashbackId, cancellationToken);
            ValidarSnapshotsPersistidos(pagamentoPix, cashback);

            if (EstadoJaAplicado(pagamentoPix, cashback, resultadoConclusivo.Value))
            {
                await transaction.CommitAsync(cancellationToken);
                return ResultadoPersistenciaAplicacaoPagamentoPix.JaAplicado(resultadoConclusivo.Value);
            }

            ValidarEstadoInicial(pagamentoPix, cashback);
            var statusPagamentoPixFinal = ObterStatusPagamentoPixFinal(
                resultadoConclusivo.Value,
                pagamentoPix.QuantidadeTentativas);

            if (await AtualizarPagamentoPixAsync(
                    connection,
                    transaction,
                    pagamentoPixId,
                    pagamentoPix.QuantidadeTentativas,
                    statusPagamentoPixFinal,
                    cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "A atualização condicional do Pagamento Pix não foi aplicada e requer intervenção técnica.");
            }

            if (resultadoConclusivo == ResultadoOperacaoPagamentoPix.Confirmado &&
                await AtualizarCashbackAsync(connection, transaction, pagamentoPix.CashbackId, cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "A atualização condicional do Cashback não foi aplicada e a transação foi revertida.");
            }

            await transaction.CommitAsync(cancellationToken);
            return ResultadoPersistenciaAplicacaoPagamentoPix.Aplicado(resultadoConclusivo.Value);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
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

    private static async Task<IReadOnlyCollection<OperacaoPersistida>> ObterOperacoesParaAtualizacaoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT tipo_operacao, numero_tentativa_envio, resultado, started_at, finished_at
            FROM operacoes_pagamento_pix
            WHERE pagamento_pix_id = @pagamentoPixId
            ORDER BY started_at, id
            FOR UPDATE;
            """;

        var operacoes = new List<OperacaoPersistida>();
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@pagamentoPixId", pagamentoPixId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tentativaOrdinal = reader.GetOrdinal("numero_tentativa_envio");
            var resultadoOrdinal = reader.GetOrdinal("resultado");
            var finalizadaOrdinal = reader.GetOrdinal("finished_at");
            operacoes.Add(new OperacaoPersistida(
                ObterEnum<TipoOperacaoPagamentoPix>(reader, "tipo_operacao"),
                reader.IsDBNull(tentativaOrdinal) ? null : reader.GetInt32(tentativaOrdinal),
                reader.IsDBNull(resultadoOrdinal) ? null : ObterEnum<ResultadoOperacaoPagamentoPix>(reader, "resultado"),
                EmUtc(reader.GetDateTime(reader.GetOrdinal("started_at"))),
                reader.IsDBNull(finalizadaOrdinal) ? null : EmUtc(reader.GetDateTime(finalizadaOrdinal))));
        }

        return operacoes.AsReadOnly();
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

    private static CicloAtual IdentificarCicloAtual(
        IReadOnlyCollection<OperacaoPersistida> historico,
        int tentativaAtual)
    {
        var enviosDaTentativaAtual = historico
            .Where(operacao =>
                operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio &&
                operacao.NumeroTentativaEnvio == tentativaAtual)
            .ToArray();
        if (enviosDaTentativaAtual.Length != 1)
        {
            throw new InvalidOperationException(
                "Pagamento Pix deve possuir exatamente um envio para a tentativa atual.");
        }

        if (historico.Any(operacao =>
                operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio &&
                operacao.NumeroTentativaEnvio < tentativaAtual &&
                !operacao.FinishedAt.HasValue))
        {
            throw new InvalidOperationException(
                "Pagamento Pix possui envio aberto de tentativa anterior e requer intervenção técnica.");
        }

        var envioAtual = enviosDaTentativaAtual[0];
        return new CicloAtual(
            envioAtual,
            historico
                .Where(operacao =>
                    operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta &&
                    operacao.CreatedAt > envioAtual.CreatedAt)
                .ToArray());
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

    private static void ValidarSnapshotsPersistidos(PagamentoPixPersistido pagamentoPix, CashbackPersistido cashback)
    {
        if (pagamentoPix.UsuarioBeneficiarioId != cashback.UsuarioIndicadorId ||
            pagamentoPix.Valor != cashback.Valor)
        {
            throw new InvalidOperationException(
                "Os snapshots persistidos de Pagamento Pix e Cashback são incompatíveis e requerem intervenção técnica.");
        }
    }

    private static bool EstadoJaAplicado(
        PagamentoPixPersistido pagamentoPix,
        CashbackPersistido cashback,
        ResultadoOperacaoPagamentoPix resultadoConclusivo) =>
        resultadoConclusivo switch
        {
            ResultadoOperacaoPagamentoPix.Confirmado =>
                pagamentoPix.Status == StatusPagamentoPix.Concluido && cashback.Status == StatusCashback.Pago,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada =>
                (pagamentoPix.Status is StatusPagamentoPix.Falhou or StatusPagamentoPix.FalhaDefinitiva) &&
                cashback.Status == StatusCashback.Disponivel &&
                StatusFalhaCoerente(pagamentoPix.Status, pagamentoPix.QuantidadeTentativas),
            _ => false
        };

    private static void ValidarEstadoInicial(PagamentoPixPersistido pagamentoPix, CashbackPersistido cashback)
    {
        if (pagamentoPix.Status != StatusPagamentoPix.Processando ||
            cashback.Status != StatusCashback.Disponivel)
        {
            throw new InvalidOperationException(
                "O estado persistido é incompatível com a aplicação segura do resultado financeiro.");
        }
    }

    private static StatusPagamentoPix ObterStatusPagamentoPixFinal(
        ResultadoOperacaoPagamentoPix resultadoConclusivo,
        int quantidadeTentativas) =>
        resultadoConclusivo switch
        {
            ResultadoOperacaoPagamentoPix.Confirmado => StatusPagamentoPix.Concluido,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada when quantidadeTentativas is >= 1 and < 5 => StatusPagamentoPix.Falhou,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada when quantidadeTentativas == 5 => StatusPagamentoPix.FalhaDefinitiva,
            _ => throw new InvalidOperationException("A combinação entre evidência conclusiva e tentativa é inválida.")
        };

    private static bool StatusFalhaCoerente(StatusPagamentoPix status, int quantidadeTentativas) =>
        (status == StatusPagamentoPix.Falhou && quantidadeTentativas is >= 1 and < 5) ||
        (status == StatusPagamentoPix.FalhaDefinitiva && quantidadeTentativas == 5);

    private static async Task<int> AtualizarPagamentoPixAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        int quantidadeTentativas,
        StatusPagamentoPix statusFinal,
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
        AdicionarGuid(command, "@id", pagamentoPixId);
        command.Parameters.Add("@statusFinal", MySqlDbType.Int32).Value = (int)statusFinal;
        command.Parameters.Add("@statusProcessando", MySqlDbType.Int32).Value = (int)StatusPagamentoPix.Processando;
        command.Parameters.Add("@quantidadeTentativas", MySqlDbType.Int32).Value = quantidadeTentativas;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = DateTime.UtcNow;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> AtualizarCashbackAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid cashbackId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE cashbacks
            SET status = @statusFinal, updated_at = @updatedAt
            WHERE id = @id
              AND status = @statusDisponivel;
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", cashbackId);
        command.Parameters.Add("@statusFinal", MySqlDbType.Int32).Value = (int)StatusCashback.Pago;
        command.Parameters.Add("@statusDisponivel", MySqlDbType.Int32).Value = (int)StatusCashback.Disponivel;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = DateTime.UtcNow;
        return await command.ExecuteNonQueryAsync(cancellationToken);
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

    private sealed record PagamentoPixPersistido(
        Guid CashbackId,
        Guid UsuarioBeneficiarioId,
        decimal Valor,
        StatusPagamentoPix Status,
        int QuantidadeTentativas);

    private sealed record CashbackPersistido(Guid UsuarioIndicadorId, decimal Valor, StatusCashback Status);

    private sealed record OperacaoPersistida(
        TipoOperacaoPagamentoPix TipoOperacao,
        int? NumeroTentativaEnvio,
        ResultadoOperacaoPagamentoPix? Resultado,
        DateTime CreatedAt,
        DateTime? FinishedAt);

    private sealed record CicloAtual(OperacaoPersistida Envio, IReadOnlyCollection<OperacaoPersistida> Consultas);
}

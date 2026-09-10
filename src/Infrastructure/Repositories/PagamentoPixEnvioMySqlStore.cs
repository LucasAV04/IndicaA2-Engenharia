using Application.Interfaces.Stores;
using Application.Interfaces.Providers;
using Application.Models;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

/// <summary>
/// Coordena uma tentativa de envio sob um lease persistente. Nenhuma transação
/// permanece aberta durante a chamada externa ao provider.
/// </summary>
public sealed class PagamentoPixEnvioMySqlStore : IPagamentoPixEnvioStore
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public PagamentoPixEnvioMySqlStore(MySqlConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task<PreparacaoEnvioPagamentoPixResult> TentarPrepararEnvioAsync(
        Guid pagamentoPixId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pagamentoPixId, Guid.Empty);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var pagamento = await ObterPagamentoParaCoordenacaoAsync(connection, transaction, pagamentoPixId, cancellationToken);
            var agora = await ObterAgoraAsync(connection, transaction, cancellationToken);
            ValidarLeases(pagamento);

            var operacoes = await ObterOperacoesAsync(connection, transaction, pagamentoPixId, cancellationToken);
            if (pagamento.Status == StatusPagamentoPix.Processando)
            {
                var recuperacao = await TentarRecuperarEnvioAbertoAsync(
                    connection, transaction, pagamentoPixId, pagamento, operacoes, agora, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return recuperacao;
            }

            if (!PagamentoPix.StatusElegiveisParaIniciarTentativa.Contains(pagamento.Status) ||
                pagamento.QuantidadeTentativas >= PagamentoPix.TentativasMaximas ||
                pagamento.EnvioLeaseId.HasValue || pagamento.ReconciliacaoLeaseId.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return PreparacaoEnvioPagamentoPixResult.NaoAdquirido();
            }

            if (operacoes.Any(operacao =>
                    operacao.Tipo == TipoOperacaoPagamentoPix.Consulta && !operacao.FinishedAt.HasValue))
            {
                throw new InvalidOperationException("Pagamento Pix possui consulta aberta incompatível com um novo envio.");
            }
            if (operacoes.Any(operacao =>
                    operacao.Tipo == TipoOperacaoPagamentoPix.Envio && !operacao.FinishedAt.HasValue))
            {
                throw new InvalidOperationException("Pagamento Pix possui envio aberto incompatível com uma nova tentativa.");
            }

            var leaseId = Guid.NewGuid();
            var tentativa = pagamento.QuantidadeTentativas + 1;
            var operacao = OperacaoPagamentoPix.IniciarEnvio(pagamentoPixId, tentativa);
            if (!await AtualizarPreparacaoAsync(
                    connection, transaction, pagamentoPixId, pagamento.Status, pagamento.QuantidadeTentativas,
                    leaseId, agora, cancellationToken))
            {
                throw new InvalidOperationException("A preparação do envio Pix perdeu a coordenação persistente.");
            }

            await AdicionarOperacaoAsync(connection, transaction, operacao, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PreparacaoEnvioPagamentoPixResult.AdquiridoCom(
                operacao.Id, tentativa, leaseId, operacao.ReferenciaIdempotente);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<FinalizacaoEnvioPagamentoPixResult> FinalizarEnvioAsync(
        Guid pagamentoPixId,
        Guid operacaoEnvioId,
        Guid leaseId,
        ResultadoOperacaoPagamentoPix resultado,
        string? identificadorProvider,
        string? codigo,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pagamentoPixId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(operacaoEnvioId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(leaseId, Guid.Empty);
        if (!Enum.IsDefined(resultado))
            throw new ArgumentOutOfRangeException(nameof(resultado));

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var pagamento = await ObterPagamentoParaCoordenacaoAsync(connection, transaction, pagamentoPixId, cancellationToken);
            ValidarLeases(pagamento);
            var operacoes = await ObterOperacoesAsync(connection, transaction, pagamentoPixId, cancellationToken);
            var agora = await ObterAgoraAsync(connection, transaction, cancellationToken);

            if (pagamento.Status != StatusPagamentoPix.Processando ||
                pagamento.EnvioLeaseId != leaseId ||
                !pagamento.EnvioLeaseExpiraEm.HasValue ||
                pagamento.EnvioLeaseExpiraEm.Value <= agora ||
                pagamento.ReconciliacaoLeaseId.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinalizacaoEnvioPagamentoPixResult(false);
            }

            var enviosAtuais = operacoes.Where(operacao =>
                operacao.Tipo == TipoOperacaoPagamentoPix.Envio &&
                operacao.NumeroTentativa == pagamento.QuantidadeTentativas).ToArray();
            if (enviosAtuais.Length != 1 || enviosAtuais[0].Id != operacaoEnvioId || enviosAtuais[0].FinishedAt.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FinalizacaoEnvioPagamentoPixResult(false);
            }

            var envio = enviosAtuais[0];
            if (envio.PagamentoPixId != pagamentoPixId ||
                envio.Tipo != TipoOperacaoPagamentoPix.Envio ||
                envio.NumeroTentativa != pagamento.QuantidadeTentativas ||
                envio.Resultado.HasValue ||
                !string.IsNullOrWhiteSpace(envio.IdentificadorProvider) ||
                !string.IsNullOrWhiteSpace(envio.Codigo) ||
                !string.Equals(envio.ReferenciaIdempotente, PixReferenciaIdempotente.Criar(pagamentoPixId), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A auditoria de envio persistida está inconsistente e não pode ser finalizada.");
            }

            var auditoria = OperacaoPagamentoPix.Reidratar(
                envio.Id, envio.PagamentoPixId, envio.Tipo, envio.NumeroTentativa,
                envio.ReferenciaIdempotente, envio.Resultado, envio.IdentificadorProvider, envio.Codigo,
                envio.CreatedAt, envio.UpdatedAt, envio.FinishedAt);
            auditoria.Finalizar(resultado, identificadorProvider, codigo);

            if (!await FinalizarOperacaoAsync(
                    connection, transaction, envio, auditoria, cancellationToken) ||
                !await LiberarLeaseAsync(connection, transaction, pagamentoPixId, leaseId, cancellationToken))
            {
                throw new InvalidOperationException("A finalização condicional do envio Pix não pôde ser persistida.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new FinalizacaoEnvioPagamentoPixResult(true);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<PagamentoCoordenado> ObterPagamentoParaCoordenacaoAsync(
        MySqlConnection connection, MySqlTransaction transaction, Guid pagamentoPixId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT status, quantidade_tentativas,
                   envio_lease_id, envio_lease_expira_em,
                   reconciliacao_lease_id, reconciliacao_lease_expira_em
            FROM pagamentos_pix
            WHERE id = @id
            FOR UPDATE;
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("O Pagamento Pix não foi encontrado para preparação do envio.");

        return new PagamentoCoordenado(
            ObterEnum<StatusPagamentoPix>(reader, "status"),
            reader.GetInt32(reader.GetOrdinal("quantidade_tentativas")),
            reader.ObterGuidOpcional("envio_lease_id"), ObterDataOpcionalUtc(reader, "envio_lease_expira_em"),
            reader.ObterGuidOpcional("reconciliacao_lease_id"), ObterDataOpcionalUtc(reader, "reconciliacao_lease_expira_em"));
    }

    private static async Task<IReadOnlyCollection<OperacaoCoordenada>> ObterOperacoesAsync(
        MySqlConnection connection, MySqlTransaction transaction, Guid pagamentoPixId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, pagamento_pix_id, tipo_operacao, numero_tentativa_envio,
                   referencia_idempotente, resultado, identificador_provider, codigo,
                   started_at, updated_at, finished_at
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
            var tentativa = reader.GetOrdinal("numero_tentativa_envio");
            var resultado = reader.GetOrdinal("resultado");
            var finalizada = reader.GetOrdinal("finished_at");
            operacoes.Add(new OperacaoCoordenada(
                reader.ObterGuid("id"), reader.ObterGuid("pagamento_pix_id"),
                ObterEnum<TipoOperacaoPagamentoPix>(reader, "tipo_operacao"),
                reader.IsDBNull(tentativa) ? null : reader.GetInt32(tentativa),
                reader.GetString(reader.GetOrdinal("referencia_idempotente")),
                reader.IsDBNull(resultado) ? null : ObterEnum<ResultadoOperacaoPagamentoPix>(reader, "resultado"),
                ObterTextoOpcional(reader, "identificador_provider"),
                ObterTextoOpcional(reader, "codigo"),
                EmUtc(reader.GetDateTime(reader.GetOrdinal("started_at"))),
                EmUtc(reader.GetDateTime(reader.GetOrdinal("updated_at"))),
                reader.IsDBNull(finalizada) ? null : EmUtc(reader.GetDateTime(finalizada))));
        }
        return operacoes.AsReadOnly();
    }

    private static async Task<bool> AtualizarPreparacaoAsync(
        MySqlConnection connection, MySqlTransaction transaction, Guid pagamentoPixId,
        StatusPagamentoPix statusAtual, int tentativasAtuais, Guid leaseId, DateTime agora,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pagamentos_pix
            SET status = @statusProcessando,
                quantidade_tentativas = quantidade_tentativas + 1,
                envio_lease_id = @leaseId,
                envio_lease_expira_em = DATE_ADD(@agora, INTERVAL 5 MINUTE),
                updated_at = @agora
            WHERE id = @id
              AND status = @statusAtual
              AND quantidade_tentativas = @tentativasAtuais
              AND envio_lease_id IS NULL AND envio_lease_expira_em IS NULL
              AND reconciliacao_lease_id IS NULL AND reconciliacao_lease_expira_em IS NULL;
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        AdicionarGuid(command, "@leaseId", leaseId);
        command.Parameters.Add("@statusProcessando", MySqlDbType.Int32).Value = (int)StatusPagamentoPix.Processando;
        command.Parameters.Add("@statusAtual", MySqlDbType.Int32).Value = (int)statusAtual;
        command.Parameters.Add("@tentativasAtuais", MySqlDbType.Int32).Value = tentativasAtuais;
        command.Parameters.Add("@agora", MySqlDbType.DateTime).Value = agora;
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<PreparacaoEnvioPagamentoPixResult> TentarRecuperarEnvioAbertoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        PagamentoCoordenado pagamento,
        IReadOnlyCollection<OperacaoCoordenada> operacoes,
        DateTime agora,
        CancellationToken cancellationToken)
    {
        if (pagamento.ReconciliacaoLeaseId.HasValue)
            return PreparacaoEnvioPagamentoPixResult.NaoAdquirido();

        var enviosAtuais = operacoes.Where(operacao =>
            operacao.Tipo == TipoOperacaoPagamentoPix.Envio &&
            operacao.NumeroTentativa == pagamento.QuantidadeTentativas).ToArray();
        if (enviosAtuais.Length != 1)
            throw new InvalidOperationException("Pagamento Pix Processando deve possuir exatamente um envio no ciclo atual.");

        var envio = enviosAtuais[0];
        if (envio.FinishedAt.HasValue)
            return PreparacaoEnvioPagamentoPixResult.NaoAdquirido();

        if (operacoes.Any(operacao =>
                operacao.Tipo == TipoOperacaoPagamentoPix.Consulta && !operacao.FinishedAt.HasValue))
        {
            return PreparacaoEnvioPagamentoPixResult.NaoAdquirido();
        }

        if (!pagamento.EnvioLeaseId.HasValue || !pagamento.EnvioLeaseExpiraEm.HasValue)
        {
            throw new InvalidOperationException(
                "Envio aberto sem lease persistido exige regularização explícita antes da recuperação.");
        }

        if (pagamento.EnvioLeaseExpiraEm.Value > agora)
            return PreparacaoEnvioPagamentoPixResult.NaoAdquirido();

        if (envio.PagamentoPixId != pagamentoPixId ||
            envio.Resultado.HasValue ||
            !string.IsNullOrWhiteSpace(envio.IdentificadorProvider) ||
            !string.IsNullOrWhiteSpace(envio.Codigo) ||
            !string.Equals(envio.ReferenciaIdempotente, PixReferenciaIdempotente.Criar(pagamentoPixId), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("O envio aberto persistido está inconsistente e não pode ser recuperado.");
        }

        var novoLeaseId = Guid.NewGuid();
        if (!await AssumirLeaseEnvioExpiradoAsync(
                connection, transaction, pagamentoPixId, pagamento, novoLeaseId, cancellationToken))
        {
            throw new InvalidOperationException("O lease expirado de envio não pôde ser recuperado com segurança.");
        }

        return PreparacaoEnvioPagamentoPixResult.AdquiridoCom(
            envio.Id,
            envio.NumeroTentativa!.Value,
            novoLeaseId,
            envio.ReferenciaIdempotente);
    }

    private static async Task<bool> AssumirLeaseEnvioExpiradoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        PagamentoCoordenado pagamento,
        Guid novoLeaseId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pagamentos_pix
            SET envio_lease_id = @novoLeaseId,
                envio_lease_expira_em = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 5 MINUTE),
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @id
              AND status = @statusProcessando
              AND quantidade_tentativas = @quantidadeTentativas
              AND envio_lease_id = @leaseIdAnterior
              AND envio_lease_expira_em <= UTC_TIMESTAMP(6)
              AND reconciliacao_lease_id IS NULL
              AND reconciliacao_lease_expira_em IS NULL;
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        AdicionarGuid(command, "@novoLeaseId", novoLeaseId);
        AdicionarGuid(command, "@leaseIdAnterior", pagamento.EnvioLeaseId!.Value);
        command.Parameters.Add("@statusProcessando", MySqlDbType.Int32).Value = (int)StatusPagamentoPix.Processando;
        command.Parameters.Add("@quantidadeTentativas", MySqlDbType.Int32).Value = pagamento.QuantidadeTentativas;
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<bool> FinalizarOperacaoAsync(
        MySqlConnection connection, MySqlTransaction transaction, OperacaoCoordenada envio,
        OperacaoPagamentoPix auditoria,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE operacoes_pagamento_pix
            SET resultado = @resultado,
                identificador_provider = COALESCE(NULLIF(@identificadorProvider, ''), identificador_provider),
                codigo = COALESCE(NULLIF(@codigo, ''), codigo),
                finished_at = GREATEST(started_at, UTC_TIMESTAMP(6)),
                updated_at = GREATEST(started_at, UTC_TIMESTAMP(6))
            WHERE id = @id
              AND pagamento_pix_id = @pagamentoPixId
              AND tipo_operacao = @tipoOperacao
              AND numero_tentativa_envio = @numeroTentativaEnvio
              AND referencia_idempotente = @referenciaIdempotente
              AND resultado IS NULL
              AND identificador_provider IS NULL
              AND codigo IS NULL
              AND started_at = @startedAt
              AND updated_at = @updatedAt
              AND finished_at IS NULL;
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", envio.Id);
        AdicionarGuid(command, "@pagamentoPixId", envio.PagamentoPixId);
        command.Parameters.Add("@tipoOperacao", MySqlDbType.Int32).Value = (int)envio.Tipo;
        command.Parameters.Add("@numeroTentativaEnvio", MySqlDbType.Int32).Value = envio.NumeroTentativa!.Value;
        command.Parameters.Add("@referenciaIdempotente", MySqlDbType.VarChar).Value = envio.ReferenciaIdempotente;
        command.Parameters.Add("@startedAt", MySqlDbType.DateTime).Value = envio.CreatedAt;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = envio.UpdatedAt;
        command.Parameters.Add("@resultado", MySqlDbType.Int32).Value = (int)auditoria.Resultado!.Value;
        command.Parameters.Add("@identificadorProvider", MySqlDbType.VarChar).Value = NormalizarOpcional(auditoria.IdentificadorProvider) ?? (object)DBNull.Value;
        command.Parameters.Add("@codigo", MySqlDbType.VarChar).Value = NormalizarOpcional(auditoria.Codigo) ?? (object)DBNull.Value;
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<bool> LiberarLeaseAsync(
        MySqlConnection connection, MySqlTransaction transaction, Guid pagamentoPixId, Guid leaseId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pagamentos_pix
            SET envio_lease_id = NULL, envio_lease_expira_em = NULL
            WHERE id = @id AND envio_lease_id = @leaseId
              AND envio_lease_expira_em > UTC_TIMESTAMP(6);
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        AdicionarGuid(command, "@leaseId", leaseId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task AdicionarOperacaoAsync(
        MySqlConnection connection, MySqlTransaction transaction, OperacaoPagamentoPix operacao,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operacoes_pagamento_pix (
                id, pagamento_pix_id, tipo_operacao, numero_tentativa_envio,
                referencia_idempotente, resultado, identificador_provider, codigo,
                started_at, finished_at, updated_at)
            VALUES (@id, @pagamentoPixId, @tipoOperacao, @numeroTentativaEnvio,
                @referenciaIdempotente, NULL, NULL, NULL, @startedAt, NULL, @updatedAt);
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

    private static async Task<DateTime> ObterAgoraAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("SELECT UTC_TIMESTAMP(6);", connection, transaction);
        return EmUtc((DateTime)(await command.ExecuteScalarAsync(cancellationToken))!);
    }

    private static void ValidarLeases(PagamentoCoordenado pagamento)
    {
        if (pagamento.EnvioLeaseId.HasValue != pagamento.EnvioLeaseExpiraEm.HasValue ||
            pagamento.ReconciliacaoLeaseId.HasValue != pagamento.ReconciliacaoLeaseExpiraEm.HasValue)
            throw new InvalidOperationException("Pagamento Pix possui lease parcialmente preenchido e requer regularização explícita.");
        if (pagamento.EnvioLeaseId.HasValue && pagamento.ReconciliacaoLeaseId.HasValue)
            throw new InvalidOperationException("Pagamento Pix possui leases de envio e reconciliação simultâneos.");
    }

    private static TEnum ObterEnum<TEnum>(MySqlDataReader reader, string coluna) where TEnum : struct, Enum
    {
        var valor = reader.GetInt32(reader.GetOrdinal(coluna));
        return Enum.IsDefined(typeof(TEnum), valor)
            ? (TEnum)Enum.ToObject(typeof(TEnum), valor)
            : throw new InvalidOperationException("O estado persistido do Pagamento Pix é inválido.");
    }

    private static DateTime? ObterDataOpcionalUtc(MySqlDataReader reader, string coluna) =>
        reader.IsDBNull(reader.GetOrdinal(coluna)) ? null : EmUtc(reader.GetDateTime(reader.GetOrdinal(coluna)));

    private static DateTime EmUtc(DateTime data) => DateTime.SpecifyKind(data, DateTimeKind.Utc);
    private static string? ObterTextoOpcional(MySqlDataReader reader, string coluna) =>
        reader.IsDBNull(reader.GetOrdinal(coluna)) ? null : reader.GetString(reader.GetOrdinal(coluna));
    private static string? NormalizarOpcional(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) => command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private sealed record PagamentoCoordenado(
        StatusPagamentoPix Status, int QuantidadeTentativas,
        Guid? EnvioLeaseId, DateTime? EnvioLeaseExpiraEm,
        Guid? ReconciliacaoLeaseId, DateTime? ReconciliacaoLeaseExpiraEm);

    private sealed record OperacaoCoordenada(
        Guid Id,
        Guid PagamentoPixId,
        TipoOperacaoPagamentoPix Tipo,
        int? NumeroTentativa,
        string ReferenciaIdempotente,
        ResultadoOperacaoPagamentoPix? Resultado,
        string? IdentificadorProvider,
        string? Codigo,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? FinishedAt);
}

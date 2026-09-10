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
            ValidarLeases(pagamentoPix);
            if (pagamentoPix.Status != StatusPagamentoPix.Processando &&
                (pagamentoPix.EnvioLeaseId.HasValue || pagamentoPix.LeaseId.HasValue))
            {
                throw new InvalidOperationException("Pagamento Pix terminal não pode manter lease pendente.");
            }
            if (pagamentoPix.Status != StatusPagamentoPix.Processando)
            {
                await transaction.CommitAsync(cancellationToken);
                return PreparacaoReconciliacaoPagamentoPixResult.NaoAplicavel();
            }

            var operacoes = await ObterOperacoesParaCoordenacaoAsync(
                connection, transaction, pagamentoPixId, cancellationToken);
            var cicloAtual = IdentificarCicloAtual(operacoes, pagamentoPix.QuantidadeTentativas);
            pagamentoPix = pagamentoPix with { Agora = await ObterAgoraAsync(connection, transaction, cancellationToken) };
            var resultadoConclusivo = ObterResultadoConclusivo(cicloAtual);

            if (pagamentoPix.EnvioLeaseId.HasValue)
            {
                if (cicloAtual.Envio.FinishedAt.HasValue)
                    throw new InvalidOperationException("Lease de envio válido requer uma auditoria de envio aberta correspondente.");

                if (LeaseEnvioEstaValido(pagamentoPix))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return PreparacaoReconciliacaoPagamentoPixResult.ConsultaEmAndamento();
                }

                if (!await InvalidarLeaseEnvioExpiradoAsync(
                        connection, transaction, pagamentoPixId, pagamentoPix.EnvioLeaseId.Value, cancellationToken))
                {
                    throw new InvalidOperationException("O lease expirado de envio não pôde ser invalidado para reconciliação.");
                }
                pagamentoPix = pagamentoPix with { EnvioLeaseId = null, EnvioLeaseExpiraEm = null };
            }
            else if (!cicloAtual.Envio.FinishedAt.HasValue)
            {
                throw new InvalidOperationException(
                    "Envio legado aberto sem lease exige regularização auditada antes da reconciliação.");
            }

            var consultasAbertas = cicloAtual.Consultas
                .Where(operacao => !operacao.FinishedAt.HasValue)
                .ToArray();
            if (consultasAbertas.Length > 1)
            {
                throw new InvalidOperationException(
                    "Pagamento Pix possui mais de uma consulta aberta no ciclo atual.");
            }

            if (consultasAbertas.Length == 1 && LeaseEstaValido(pagamentoPix))
            {
                await transaction.CommitAsync(cancellationToken);
                return PreparacaoReconciliacaoPagamentoPixResult.ConsultaEmAndamento();
            }

            if (consultasAbertas.Length == 1 && !pagamentoPix.LeaseId.HasValue)
                throw new InvalidOperationException("Consulta aberta sem lease: requer regularização explícita antes da recuperação.");

            if (consultasAbertas.Length == 1 && !resultadoConclusivo.HasValue)
            {
                var leaseId = Guid.NewGuid();
                await AssumirLeaseAsync(connection, transaction, pagamentoPixId, leaseId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(consultasAbertas[0].Id, leaseId);
            }

            if (resultadoConclusivo.HasValue)
            {
                if (LeaseEstaValido(pagamentoPix))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return PreparacaoReconciliacaoPagamentoPixResult.ConsultaEmAndamento();
                }
                Guid? leaseRecuperado = null;
                if (pagamentoPix.LeaseId.HasValue)
                {
                    leaseRecuperado = Guid.NewGuid();
                    await AssumirLeaseAsync(connection, transaction, pagamentoPixId, leaseRecuperado.Value, cancellationToken);
                    // Sem HTTP adicional: preserva a identidade da Consulta abandonada,
                    // registrando Indeterminado porque ela não obteve resposta confiável.
                    if (consultasAbertas.Length == 1)
                        await FinalizarOperacaoAbertaAsync(connection, transaction, consultasAbertas[0].Id,
                            ResultadoOperacaoPagamentoPix.Indeterminado, null, null, cancellationToken);
                }
                var evidenciaConclusiva = ObterEvidenciaConclusiva(cicloAtual)!;
                var envioResolvido = false;
                if (!cicloAtual.Envio.FinishedAt.HasValue)
                {
                    envioResolvido = await FinalizarEnvioAbertoAsync(
                        connection,
                        transaction,
                        cicloAtual.Envio.Id,
                        resultadoConclusivo.Value,
                        evidenciaConclusiva.IdentificadorProvider,
                        evidenciaConclusiva.Codigo,
                        cancellationToken);
                    if (!envioResolvido)
                    {
                        throw new InvalidOperationException(
                            "A finalização do envio aberto não pôde ser coordenada e requer intervenção técnica.");
                    }
                }

                if (leaseRecuperado.HasValue)
                    await LiberarLeaseAsync(connection, transaction, pagamentoPixId, leaseRecuperado.Value, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PreparacaoReconciliacaoPagamentoPixResult.JaConclusivo(
                    resultadoConclusivo.Value,
                    envioResolvido);
            }

            if (LeaseEstaValido(pagamentoPix))
            {
                throw new InvalidOperationException(
                    "Pagamento Pix possui lease de reconciliação sem consulta aberta.");
            }

            var novoLeaseId = Guid.NewGuid();
            await AssumirLeaseAsync(connection, transaction, pagamentoPixId, novoLeaseId, cancellationToken);
            var consulta = OperacaoPagamentoPix.IniciarConsulta(pagamentoPixId);
            await AdicionarConsultaAsync(connection, transaction, consulta, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PreparacaoReconciliacaoPagamentoPixResult.ConsultaPreparada(consulta.Id, novoLeaseId);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<FinalizacaoConsultaPagamentoPixResult> FinalizarConsultaAsync(
        Guid pagamentoPixId,
        Guid operacaoConsultaId,
        Guid leaseId,
        ResultadoOperacaoPagamentoPix resultado,
        string? identificadorProvider,
        string? codigo,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pagamentoPixId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(operacaoConsultaId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(leaseId, Guid.Empty);
        if (!Enum.IsDefined(resultado))
            throw new ArgumentOutOfRangeException(nameof(resultado));

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var pagamentoPix = await ObterPagamentoPixParaCoordenacaoAsync(
                connection, transaction, pagamentoPixId, cancellationToken);
            ValidarLeases(pagamentoPix);
            if (pagamentoPix.Status != StatusPagamentoPix.Processando ||
                pagamentoPix.LeaseId != leaseId ||
                !LeaseEstaValido(pagamentoPix) ||
                pagamentoPix.EnvioLeaseId.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(false, false);
            }

            var operacoes = await ObterOperacoesParaCoordenacaoAsync(
                connection, transaction, pagamentoPixId, cancellationToken);
            var cicloAtual = IdentificarCicloAtual(operacoes, pagamentoPix.QuantidadeTentativas);
            pagamentoPix = pagamentoPix with { Agora = await ObterAgoraAsync(connection, transaction, cancellationToken) };
            if (!LeaseEstaValido(pagamentoPix))
            {
                await transaction.CommitAsync(cancellationToken);
                return new(false, false);
            }
            var evidencia = ObterResultadoConclusivo(cicloAtual);
            if (EhConclusivo(resultado) && evidencia.HasValue && evidencia.Value != resultado)
                throw new InvalidOperationException("Resposta conclusiva conflitante com a evidência persistida do ciclo atual.");
            var consulta = cicloAtual.Consultas.SingleOrDefault(operacao => operacao.Id == operacaoConsultaId);
            if (consulta is null || consulta.FinishedAt.HasValue ||
                cicloAtual.Consultas.Count(operacao => !operacao.FinishedAt.HasValue) != 1)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(false, false);
            }

            var auditoria = OperacaoPagamentoPix.Reidratar(consulta.Id, pagamentoPixId,
                TipoOperacaoPagamentoPix.Consulta, null, pagamentoPixId.ToString("N"),
                null, null, null, consulta.CreatedAt, consulta.CreatedAt, null);
            auditoria.Finalizar(resultado, identificadorProvider, codigo);
            if (!await FinalizarOperacaoAbertaAsync(
                    connection,
                    transaction,
                    operacaoConsultaId,
                    resultado,
                    identificadorProvider,
                    codigo,
                    cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return new(false, false);
            }

            if (EhConclusivo(resultado) && !cicloAtual.Envio.FinishedAt.HasValue &&
                !await FinalizarEnvioAbertoAsync(
                    connection,
                    transaction,
                    cicloAtual.Envio.Id,
                    resultado,
                    identificadorProvider,
                    codigo,
                    cancellationToken))
            {
                throw new InvalidOperationException("O envio aberto não pôde ser finalizado junto da consulta conclusiva.");
            }

            await LiberarLeaseAsync(connection, transaction, pagamentoPixId, leaseId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, EhConclusivo(resultado) && !cicloAtual.Envio.FinishedAt.HasValue);
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
            SELECT status, quantidade_tentativas,
                   envio_lease_id, envio_lease_expira_em,
                   reconciliacao_lease_id, reconciliacao_lease_expira_em,
                   UTC_TIMESTAMP(6) AS agora
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
            reader.GetInt32(reader.GetOrdinal("quantidade_tentativas")),
            reader.ObterGuidOpcional("envio_lease_id"),
            ObterDataOpcionalUtc(reader, "envio_lease_expira_em"),
            reader.ObterGuidOpcional("reconciliacao_lease_id"),
            ObterDataOpcionalUtc(reader, "reconciliacao_lease_expira_em"),
            EmUtc(reader.GetDateTime(reader.GetOrdinal("agora"))));
    }

    private static async Task<IReadOnlyCollection<OperacaoCoordenada>> ObterOperacoesParaCoordenacaoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tipo_operacao, numero_tentativa_envio, resultado, identificador_provider, codigo, started_at, finished_at
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
                reader.ObterGuid("id"),
                ObterEnum<TipoOperacaoPagamentoPix>(reader, "tipo_operacao"),
                reader.IsDBNull(tentativaOrdinal) ? null : reader.GetInt32(tentativaOrdinal),
                reader.IsDBNull(resultadoOrdinal) ? null : ObterEnum<ResultadoOperacaoPagamentoPix>(reader, "resultado"),
                ObterTextoOpcional(reader, "identificador_provider"),
                ObterTextoOpcional(reader, "codigo"),
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

    private static OperacaoCoordenada? ObterEvidenciaConclusiva(CicloAtual cicloAtual) =>
        new[] { cicloAtual.Envio }
            .Concat(cicloAtual.Consultas)
            .Where(operacao => EhConclusivo(operacao.Resultado))
            .OrderByDescending(operacao => operacao.FinishedAt)
            .ThenByDescending(operacao => operacao.CreatedAt)
            .FirstOrDefault();

    private static async Task AssumirLeaseAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pagamentos_pix
            SET reconciliacao_lease_id = @leaseId,
                reconciliacao_lease_expira_em = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 5 MINUTE)
            WHERE id = @id
              AND (reconciliacao_lease_expira_em IS NULL OR reconciliacao_lease_expira_em <= UTC_TIMESTAMP(6));
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        AdicionarGuid(command, "@leaseId", leaseId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("O lease de reconciliação não pôde ser adquirido.");
        }
    }

    private static async Task<bool> InvalidarLeaseEnvioExpiradoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pagamentos_pix
            SET envio_lease_id = NULL,
                envio_lease_expira_em = NULL
            WHERE id = @id
              AND envio_lease_id = @leaseId
              AND envio_lease_expira_em <= UTC_TIMESTAMP(6);
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        AdicionarGuid(command, "@leaseId", leaseId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static Task<bool> FinalizarEnvioAbertoAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid operacaoId,
        ResultadoOperacaoPagamentoPix resultado,
        string? identificadorProvider,
        string? codigo,
        CancellationToken cancellationToken) =>
        FinalizarOperacaoAbertaAsync(connection, transaction, operacaoId, resultado,
            identificadorProvider, codigo, cancellationToken);

    private static async Task<bool> FinalizarOperacaoAbertaAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid operacaoId,
        ResultadoOperacaoPagamentoPix resultado,
        string? identificadorProvider,
        string? codigo,
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
              AND finished_at IS NULL;
            """;

        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", operacaoId);
        command.Parameters.Add("@resultado", MySqlDbType.Int32).Value = (int)resultado;
        command.Parameters.Add("@identificadorProvider", MySqlDbType.VarChar).Value =
            NormalizarOpcional(identificadorProvider) ?? (object)DBNull.Value;
        command.Parameters.Add("@codigo", MySqlDbType.VarChar).Value =
            NormalizarOpcional(codigo) ?? (object)DBNull.Value;
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task LiberarLeaseAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid pagamentoPixId,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pagamentos_pix
            SET reconciliacao_lease_id = NULL,
                reconciliacao_lease_expira_em = NULL
            WHERE id = @id
              AND reconciliacao_lease_id = @leaseId
              AND reconciliacao_lease_expira_em > UTC_TIMESTAMP(6);
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        AdicionarGuid(command, "@id", pagamentoPixId);
        AdicionarGuid(command, "@leaseId", leaseId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("O lease de reconciliação não pôde ser liberado.");
        }
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

    private static async Task<DateTime> ObterAgoraAsync(
        MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        // Um horário novo DEPOIS dos locks evita usar o início de um SELECT que ficou esperando.
        await using var command = new MySqlCommand("SELECT UTC_TIMESTAMP(6);", connection, transaction);
        return EmUtc((DateTime)(await command.ExecuteScalarAsync(cancellationToken))!);
    }

    private static bool LeaseEstaValido(PagamentoPixCoordenado pagamentoPix) =>
        pagamentoPix.LeaseId.HasValue &&
        pagamentoPix.LeaseExpiraEm.HasValue &&
        pagamentoPix.LeaseExpiraEm.Value > pagamentoPix.Agora;

    private static bool LeaseEnvioEstaValido(PagamentoPixCoordenado pagamentoPix) =>
        pagamentoPix.EnvioLeaseId.HasValue &&
        pagamentoPix.EnvioLeaseExpiraEm.HasValue &&
        pagamentoPix.EnvioLeaseExpiraEm.Value > pagamentoPix.Agora;

    private static void ValidarLeases(PagamentoPixCoordenado pagamentoPix)
    {
        if (pagamentoPix.EnvioLeaseId.HasValue != pagamentoPix.EnvioLeaseExpiraEm.HasValue ||
            pagamentoPix.LeaseId.HasValue != pagamentoPix.LeaseExpiraEm.HasValue)
        {
            throw new InvalidOperationException("Lease persistido parcialmente preenchido requer regularização explícita.");
        }

        if (pagamentoPix.EnvioLeaseId.HasValue && pagamentoPix.LeaseId.HasValue)
        {
            throw new InvalidOperationException("Leases de envio e reconciliação simultâneos são inconsistentes.");
        }
    }

    private static TEnum ObterEnum<TEnum>(MySqlDataReader reader, string coluna)
        where TEnum : struct, Enum
    {
        var valor = reader.GetInt32(reader.GetOrdinal(coluna));
        return Enum.IsDefined(typeof(TEnum), valor)
            ? (TEnum)Enum.ToObject(typeof(TEnum), valor)
            : throw new InvalidOperationException("O status financeiro persistido é inválido.");
    }

    private static DateTime EmUtc(DateTime data) => DateTime.SpecifyKind(data, DateTimeKind.Utc);

    private static DateTime? ObterDataOpcionalUtc(MySqlDataReader reader, string coluna) =>
        reader.IsDBNull(reader.GetOrdinal(coluna))
            ? null
            : EmUtc(reader.GetDateTime(reader.GetOrdinal(coluna)));

    private static string? ObterTextoOpcional(MySqlDataReader reader, string coluna) =>
        reader.IsDBNull(reader.GetOrdinal(coluna)) ? null : reader.GetString(reader.GetOrdinal(coluna));

    private static string? NormalizarOpcional(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private sealed record PagamentoPixCoordenado(
        StatusPagamentoPix Status,
        int QuantidadeTentativas,
        Guid? EnvioLeaseId,
        DateTime? EnvioLeaseExpiraEm,
        Guid? LeaseId,
        DateTime? LeaseExpiraEm,
        DateTime Agora);

    private sealed record OperacaoCoordenada(
        Guid Id,
        TipoOperacaoPagamentoPix TipoOperacao,
        int? NumeroTentativaEnvio,
        ResultadoOperacaoPagamentoPix? Resultado,
        string? IdentificadorProvider,
        string? Codigo,
        DateTime CreatedAt,
        DateTime? FinishedAt);

    private sealed record CicloAtual(OperacaoCoordenada Envio, IReadOnlyCollection<OperacaoCoordenada> Consultas);
}

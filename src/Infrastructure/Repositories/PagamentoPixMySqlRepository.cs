using System.Data;
using System.Security.Cryptography;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Infrastructure.Database;
using Infrastructure.Security;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class PagamentoPixMySqlRepository : IPagamentoPixRepository
{
    private const string ConstraintPagamentoPixPorCashback = "uq_pagamentos_pix_cashback_id";

    private const string Colunas = """
        id,
        cashback_id,
        usuario_beneficiario_id,
        valor,
        tipo_chave_pix,
        chave_pix_ciphertext,
        chave_pix_nonce,
        chave_pix_tag,
        encryption_version,
        status,
        quantidade_tentativas,
        created_at,
        updated_at
        """;

    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly IDadosPixProtector _dadosPixProtector;

    public PagamentoPixMySqlRepository(
        MySqlConnectionFactory connectionFactory,
        IDadosPixProtector dadosPixProtector)
    {
        _connectionFactory = connectionFactory;
        _dadosPixProtector = dadosPixProtector;
    }

    public async Task<PagamentoPix?> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM pagamentos_pix WHERE id = @id;";
        return await ObterUnicoAsync(sql, "@id", id, cancellationToken);
    }

    public async Task<PagamentoPix?> ObterPorCashbackIdAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM pagamentos_pix WHERE cashback_id = @cashbackId;";
        return await ObterUnicoAsync(sql, "@cashbackId", cashbackId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<PagamentoPix>> ObterPorUsuarioBeneficiarioIdAsync(
        Guid usuarioBeneficiarioId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            SELECT {Colunas}
            FROM pagamentos_pix
            WHERE usuario_beneficiario_id = @usuarioBeneficiarioId
            ORDER BY created_at, id;
            """;
        var pagamentosPix = new List<PagamentoPix>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        AdicionarGuid(command, "@usuarioBeneficiarioId", usuarioBeneficiarioId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            pagamentosPix.Add(Materializar(reader));

        return pagamentosPix.AsReadOnly();
    }

    public async Task AdicionarAsync(PagamentoPix pagamentoPix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pagamentoPix);

        const string sql = """
            INSERT INTO pagamentos_pix (
                id, cashback_id, usuario_beneficiario_id, valor, tipo_chave_pix,
                chave_pix_ciphertext, chave_pix_nonce, chave_pix_tag, encryption_version,
                status, quantidade_tentativas, created_at, updated_at)
            VALUES (
                @id, @cashbackId, @usuarioBeneficiarioId, @valor, @tipoChavePix,
                @chavePixCiphertext, @chavePixNonce, @chavePixTag, @encryptionVersion,
                @status, @quantidadeTentativas, @createdAt, @updatedAt);
            """;

        var associatedData = PagamentoPixAssociatedData.Criar(
            pagamentoPix.Id,
            pagamentoPix.CashbackId,
            pagamentoPix.UsuarioBeneficiarioId,
            pagamentoPix.Valor,
            pagamentoPix.TipoChavePix);
        var materialProtegido = _dadosPixProtector.Proteger(pagamentoPix.ChavePix, associatedData);

        try
        {
            await ExecutarComandoAsync(sql, command =>
            {
                AdicionarGuid(command, "@id", pagamentoPix.Id);
                AdicionarGuid(command, "@cashbackId", pagamentoPix.CashbackId);
                AdicionarGuid(command, "@usuarioBeneficiarioId", pagamentoPix.UsuarioBeneficiarioId);
                command.Parameters.Add("@valor", MySqlDbType.Decimal).Value = pagamentoPix.Valor;
                command.Parameters.Add("@tipoChavePix", MySqlDbType.Int32).Value = (int)pagamentoPix.TipoChavePix;
                AdicionarMaterialProtegido(command, materialProtegido);
                AdicionarParametrosMutaveis(command, pagamentoPix);
                command.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = pagamentoPix.CreatedAt;
            }, cancellationToken);
        }
        catch (MySqlException exception) when (EhViolacaoDePagamentoPixDuplicado(exception))
        {
            throw new PagamentoPixJaExisteException();
        }
    }

    public async Task AtualizarAsync(PagamentoPix pagamentoPix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pagamentoPix);

        const string sql = """
            UPDATE pagamentos_pix
            SET
                status = @status,
                quantidade_tentativas = @quantidadeTentativas,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        await ExecutarComandoAsync(sql, command =>
        {
            AdicionarGuid(command, "@id", pagamentoPix.Id);
            AdicionarParametrosMutaveis(command, pagamentoPix);
        }, cancellationToken);
    }

    public async Task<bool> TentarIniciarProcessamentoAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default)
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

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        AdicionarGuid(command, "@id", pagamentoPixId);
        command.Parameters.Add("@statusProcessando", MySqlDbType.Int32).Value = (int)StatusPagamentoPix.Processando;
        command.Parameters.Add("@tentativasMaximas", MySqlDbType.Int32).Value = PagamentoPix.TentativasMaximas;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = DateTime.UtcNow;

        foreach (var (status, indice) in statusElegiveis.Select((status, indice) => (status, indice)))
        {
            command.Parameters.Add($"@statusElegivel{indice}", MySqlDbType.Int32).Value = (int)status;
        }

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<PagamentoPix?> ObterUnicoAsync(
        string sql,
        string nomeParametro,
        Guid valorParametro,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        AdicionarGuid(command, nomeParametro, valorParametro);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Materializar(reader) : null;
    }

    private async Task ExecutarComandoAsync(
        string sql,
        Action<MySqlCommand> adicionarParametros,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        adicionarParametros(command);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool EhViolacaoDePagamentoPixDuplicado(MySqlException exception) =>
        exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry &&
        exception.Message.Contains(ConstraintPagamentoPixPorCashback, StringComparison.OrdinalIgnoreCase);

    private static void AdicionarMaterialProtegido(
        MySqlCommand command,
        DadosPixProtegido materialProtegido)
    {
        command.Parameters.Add("@chavePixCiphertext", MySqlDbType.Blob).Value = materialProtegido.Ciphertext;
        command.Parameters.Add("@chavePixNonce", MySqlDbType.VarBinary).Value = materialProtegido.Nonce;
        command.Parameters.Add("@chavePixTag", MySqlDbType.VarBinary).Value = materialProtegido.Tag;
        command.Parameters.Add("@encryptionVersion", MySqlDbType.Int32).Value = materialProtegido.EncryptionVersion;
    }

    private static void AdicionarParametrosMutaveis(MySqlCommand command, PagamentoPix pagamentoPix)
    {
        command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)pagamentoPix.Status;
        command.Parameters.Add("@quantidadeTentativas", MySqlDbType.Int32).Value = pagamentoPix.QuantidadeTentativas;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = pagamentoPix.UpdatedAt;
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private PagamentoPix Materializar(MySqlDataReader reader)
    {
        var id = reader.ObterGuid("id");
        var cashbackId = reader.ObterGuid("cashback_id");
        var usuarioBeneficiarioId = reader.ObterGuid("usuario_beneficiario_id");
        var valor = reader.GetDecimal(reader.GetOrdinal("valor"));
        var tipoChavePix = (TipoChavePix)reader.GetInt32(reader.GetOrdinal("tipo_chave_pix"));
        var associatedData = PagamentoPixAssociatedData.Criar(
            id,
            cashbackId,
            usuarioBeneficiarioId,
            valor,
            tipoChavePix);
        var materialProtegido = new DadosPixProtegido(
            ObterBytes(reader, "chave_pix_ciphertext"),
            ObterBytes(reader, "chave_pix_nonce"),
            ObterBytes(reader, "chave_pix_tag"),
            reader.GetInt32(reader.GetOrdinal("encryption_version")));

        string chavePix;
        try
        {
            chavePix = _dadosPixProtector.Desproteger(materialProtegido, associatedData);
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException(
                "Não foi possível descriptografar a chave Pix do Pagamento Pix armazenado.",
                exception);
        }

        return PagamentoPix.Reidratar(
            id,
            cashbackId,
            usuarioBeneficiarioId,
            valor,
            tipoChavePix,
            chavePix,
            (StatusPagamentoPix)reader.GetInt32(reader.GetOrdinal("status")),
            reader.GetInt32(reader.GetOrdinal("quantidade_tentativas")),
            ObterDataUtc(reader, "created_at"),
            ObterDataUtc(reader, "updated_at"));
    }

    private static byte[] ObterBytes(MySqlDataReader reader, string nomeColuna)
    {
        var ordinal = reader.GetOrdinal(nomeColuna);
        if (reader.IsDBNull(ordinal))
        {
            throw new DataException(
                $"O material criptográfico persistido na coluna '{nomeColuna}' é obrigatório.");
        }

        return reader.GetFieldValue<byte[]>(ordinal);
    }

    private static DateTime ObterDataUtc(MySqlDataReader reader, string nomeColuna) =>
        DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal(nomeColuna)), DateTimeKind.Utc);
}

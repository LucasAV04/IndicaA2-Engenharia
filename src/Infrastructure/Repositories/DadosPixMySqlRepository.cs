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

public sealed class DadosPixMySqlRepository : IDadosPixRepository
{
    private const string ConstraintDadosPixPorUsuario = "uq_dados_pix_usuario_id";

    private const string Colunas = """
        id,
        usuario_id,
        tipo_chave_pix,
        chave_pix_ciphertext,
        chave_pix_nonce,
        chave_pix_tag,
        encryption_version,
        created_at,
        updated_at
        """;

    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly IDadosPixProtector _dadosPixProtector;

    public DadosPixMySqlRepository(
        MySqlConnectionFactory connectionFactory,
        IDadosPixProtector dadosPixProtector)
    {
        _connectionFactory = connectionFactory;
        _dadosPixProtector = dadosPixProtector;
    }

    public async Task<DadosPix?> ObterPorUsuarioIdAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM dados_pix WHERE usuario_id = @usuarioId;";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        AdicionarGuid(command, "@usuarioId", usuarioId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? Materializar(reader)
            : null;
    }

    public async Task AdicionarAsync(DadosPix dadosPix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dadosPix);

        const string sql = """
            INSERT INTO dados_pix (
                id, usuario_id, tipo_chave_pix, chave_pix_ciphertext, chave_pix_nonce,
                chave_pix_tag, encryption_version, created_at, updated_at)
            VALUES (
                @id, @usuarioId, @tipoChavePix, @chavePixCiphertext, @chavePixNonce,
                @chavePixTag, @encryptionVersion, @createdAt, @updatedAt);
            """;

        var materialProtegido = _dadosPixProtector.Proteger(dadosPix.ChavePix);

        try
        {
            await ExecutarComandoAsync(sql, command =>
                AdicionarParametrosEstado(command, dadosPix, materialProtegido), cancellationToken);
        }
        catch (MySqlException exception) when (EhViolacaoDeDadosPixDuplicado(exception))
        {
            throw new DadosPixJaExisteException();
        }
    }

    public async Task AtualizarAsync(DadosPix dadosPix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dadosPix);

        const string sql = """
            UPDATE dados_pix
            SET
                tipo_chave_pix = @tipoChavePix,
                chave_pix_ciphertext = @chavePixCiphertext,
                chave_pix_nonce = @chavePixNonce,
                chave_pix_tag = @chavePixTag,
                encryption_version = @encryptionVersion,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        var materialProtegido = _dadosPixProtector.Proteger(dadosPix.ChavePix);

        await ExecutarComandoAsync(sql, command =>
        {
            AdicionarGuid(command, "@id", dadosPix.Id);
            command.Parameters.Add("@tipoChavePix", MySqlDbType.Int32).Value = (int)dadosPix.TipoChavePix;
            AdicionarMaterialProtegido(command, materialProtegido);
            command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = dadosPix.UpdatedAt;
        }, cancellationToken);
    }

    public async Task RemoverAsync(DadosPix dadosPix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dadosPix);

        const string sql = "DELETE FROM dados_pix WHERE id = @id;";
        await ExecutarComandoAsync(sql, command => AdicionarGuid(command, "@id", dadosPix.Id), cancellationToken);
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

    private static bool EhViolacaoDeDadosPixDuplicado(MySqlException exception) =>
        exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry &&
        exception.Message.Contains(ConstraintDadosPixPorUsuario, StringComparison.OrdinalIgnoreCase);

    private static void AdicionarParametrosEstado(
        MySqlCommand command,
        DadosPix dadosPix,
        DadosPixProtegido materialProtegido)
    {
        AdicionarGuid(command, "@id", dadosPix.Id);
        AdicionarGuid(command, "@usuarioId", dadosPix.UsuarioId);
        command.Parameters.Add("@tipoChavePix", MySqlDbType.Int32).Value = (int)dadosPix.TipoChavePix;
        AdicionarMaterialProtegido(command, materialProtegido);
        command.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = dadosPix.CreatedAt;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = dadosPix.UpdatedAt;
    }

    private static void AdicionarMaterialProtegido(MySqlCommand command, DadosPixProtegido materialProtegido)
    {
        command.Parameters.Add("@chavePixCiphertext", MySqlDbType.Blob).Value = materialProtegido.Ciphertext;
        command.Parameters.Add("@chavePixNonce", MySqlDbType.VarBinary).Value = materialProtegido.Nonce;
        command.Parameters.Add("@chavePixTag", MySqlDbType.VarBinary).Value = materialProtegido.Tag;
        command.Parameters.Add("@encryptionVersion", MySqlDbType.Int32).Value = materialProtegido.EncryptionVersion;
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private DadosPix Materializar(MySqlDataReader reader)
    {
        var tipoChavePixPersistido = reader.GetInt32(reader.GetOrdinal("tipo_chave_pix"));
        if (!Enum.IsDefined(typeof(TipoChavePix), tipoChavePixPersistido))
            throw new DataException($"O tipo de chave Pix persistido '{tipoChavePixPersistido}' é inválido.");

        var materialProtegido = new DadosPixProtegido(
            ObterBytes(reader, "chave_pix_ciphertext"),
            ObterBytes(reader, "chave_pix_nonce"),
            ObterBytes(reader, "chave_pix_tag"),
            reader.GetInt32(reader.GetOrdinal("encryption_version")));

        string chavePix;
        try
        {
            chavePix = _dadosPixProtector.Desproteger(materialProtegido);
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException("Não foi possível descriptografar os Dados Pix armazenados.", exception);
        }

        return DadosPix.Reidratar(
            reader.ObterGuid("id"),
            reader.ObterGuid("usuario_id"),
            (TipoChavePix)tipoChavePixPersistido,
            chavePix,
            ObterDataUtc(reader, "created_at"),
            ObterDataUtc(reader, "updated_at"));
    }

    private static byte[] ObterBytes(MySqlDataReader reader, string nomeColuna)
    {
        var ordinal = reader.GetOrdinal(nomeColuna);
        if (reader.IsDBNull(ordinal))
            throw new DataException($"O material criptográfico persistido na coluna '{nomeColuna}' é obrigatório.");

        return reader.GetFieldValue<byte[]>(ordinal);
    }

    private static DateTime ObterDataUtc(MySqlDataReader reader, string nomeColuna) =>
        DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal(nomeColuna)), DateTimeKind.Utc);
}

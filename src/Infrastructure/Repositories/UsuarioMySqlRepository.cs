using System.Data;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class UsuarioMySqlRepository : IUsuarioRepository
{
    private const string Colunas = """
        id,
        nome,
        email,
        codigo_indicacao,
        senha_hash,
        telefone,
        status,
        tipo_usuario,
        email_confirmado,
        ultimo_login,
        created_at,
        updated_at
        """;

    private readonly MySqlConnectionFactory _connectionFactory;

    public UsuarioMySqlRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM usuarios WHERE id = @id;";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        AdicionarGuid(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? Materializar(reader)
            : null;
    }

    public async Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM usuarios WHERE email = @email LIMIT 1;";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        command.Parameters.Add("@email", MySqlDbType.VarChar).Value = email;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? Materializar(reader)
            : null;
    }

    public async Task<Usuario?> ObterPorCodigoIndicacaoAsync(
        string codigoIndicacao,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM usuarios WHERE codigo_indicacao = @codigoIndicacao LIMIT 1;";
        var codigoNormalizado = Usuario.NormalizarCodigoIndicacao(codigoIndicacao);

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        command.Parameters.Add("@codigoIndicacao", MySqlDbType.VarChar).Value = codigoNormalizado;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? Materializar(reader)
            : null;
    }

    public async Task<bool> ExistePorEmailAsync(
        string email,
        Guid? ignorarUsuarioId = null,
        CancellationToken cancellationToken = default)
    {
        const string sqlComIgnorarUsuario = """
            SELECT EXISTS(
                SELECT 1
                FROM usuarios
                WHERE email = @email
                  AND id <> @ignorarUsuarioId);
            """;
        const string sqlSemIgnorarUsuario = "SELECT EXISTS(SELECT 1 FROM usuarios WHERE email = @email);";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(
            connection,
            ignorarUsuarioId.HasValue ? sqlComIgnorarUsuario : sqlSemIgnorarUsuario);
        command.Parameters.Add("@email", MySqlDbType.VarChar).Value = email;

        if (ignorarUsuarioId.HasValue)
            AdicionarGuid(command, "@ignorarUsuarioId", ignorarUsuarioId.Value);

        var resultado = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(resultado) == 1;
    }

    public async Task<IReadOnlyCollection<Usuario>> ObterTodosAsync(CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM usuarios ORDER BY created_at;";
        var usuarios = new List<Usuario>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            usuarios.Add(Materializar(reader));
        }

        return usuarios.AsReadOnly();
    }

    public async Task<bool> ExistePorIdAsync(Guid id)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM usuarios WHERE id = @id);";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync();
        await using var command = CriarComando(connection, sql);
        AdicionarGuid(command, "@id", id);

        var resultado = await command.ExecuteScalarAsync();
        return Convert.ToInt64(resultado) == 1;
    }

    public async Task AdicionarAsync(Usuario usuario, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        const string sql = """
            INSERT INTO usuarios (
                id,
                nome,
                email,
                codigo_indicacao,
                senha_hash,
                telefone,
                status,
                tipo_usuario,
                email_confirmado,
                ultimo_login,
                created_at,
                updated_at)
            VALUES (
                @id,
                @nome,
                @email,
                @codigoIndicacao,
                @senhaHash,
                @telefone,
                @status,
                @tipoUsuario,
                @emailConfirmado,
                @ultimoLogin,
                @createdAt,
                @updatedAt);
            """;

        await ExecutarComandoAsync(sql, command => AdicionarParametrosEstado(command, usuario), cancellationToken);
    }

    public async Task AtualizarAsync(Usuario usuario, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        const string sql = """
            UPDATE usuarios
            SET
                nome = @nome,
                email = @email,
                senha_hash = @senhaHash,
                telefone = @telefone,
                status = @status,
                email_confirmado = @emailConfirmado,
                ultimo_login = @ultimoLogin,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        await ExecutarComandoAsync(sql, command =>
        {
            AdicionarGuid(command, "@id", usuario.Id);
            command.Parameters.Add("@nome", MySqlDbType.VarChar).Value = usuario.Nome;
            command.Parameters.Add("@email", MySqlDbType.VarChar).Value = usuario.Email;
            command.Parameters.Add("@senhaHash", MySqlDbType.VarChar).Value = usuario.SenhaHash;
            AdicionarTextoOpcional(command, "@telefone", usuario.Telefone);
            command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)usuario.Status;
            command.Parameters.Add("@emailConfirmado", MySqlDbType.Bool).Value = usuario.EmailConfirmado;
            AdicionarDataOpcional(command, "@ultimoLogin", usuario.UltimoLogin);
            command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = usuario.UpdatedAt;
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

    private static void AdicionarParametrosEstado(MySqlCommand command, Usuario usuario)
    {
        AdicionarGuid(command, "@id", usuario.Id);
        command.Parameters.Add("@nome", MySqlDbType.VarChar).Value = usuario.Nome;
        command.Parameters.Add("@email", MySqlDbType.VarChar).Value = usuario.Email;
        AdicionarTextoOpcional(command, "@codigoIndicacao", usuario.CodigoIndicacao);
        command.Parameters.Add("@senhaHash", MySqlDbType.VarChar).Value = usuario.SenhaHash;
        AdicionarTextoOpcional(command, "@telefone", usuario.Telefone);
        command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)usuario.Status;
        command.Parameters.Add("@tipoUsuario", MySqlDbType.Int32).Value = (int)usuario.TipoUsuario;
        command.Parameters.Add("@emailConfirmado", MySqlDbType.Bool).Value = usuario.EmailConfirmado;
        AdicionarDataOpcional(command, "@ultimoLogin", usuario.UltimoLogin);
        command.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = usuario.CreatedAt;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = usuario.UpdatedAt;
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private static void AdicionarTextoOpcional(MySqlCommand command, string nome, string? valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = (object?)valor ?? DBNull.Value;

    private static void AdicionarDataOpcional(MySqlCommand command, string nome, DateTime? valor) =>
        command.Parameters.Add(nome, MySqlDbType.DateTime).Value = (object?)valor ?? DBNull.Value;

    private static Usuario Materializar(MySqlDataReader reader)
    {
        var statusPersistido = reader.GetInt32(reader.GetOrdinal("status"));
        if (!Enum.IsDefined(typeof(StatusUsuario), statusPersistido))
            throw new DataException($"O status persistido '{statusPersistido}' é inválido.");

        var tipoUsuarioPersistido = reader.GetInt32(reader.GetOrdinal("tipo_usuario"));
        if (!Enum.IsDefined(typeof(TipoUsuario), tipoUsuarioPersistido))
            throw new DataException($"O tipo de usuário persistido '{tipoUsuarioPersistido}' é inválido.");

        return Usuario.Reidratar(
            reader.ObterGuid("id"),
            reader.GetString(reader.GetOrdinal("nome")),
            reader.GetString(reader.GetOrdinal("email")),
            reader.GetString(reader.GetOrdinal("senha_hash")),
            ObterTextoOpcional(reader, "telefone"),
            (StatusUsuario)statusPersistido,
            (TipoUsuario)tipoUsuarioPersistido,
            reader.GetBoolean(reader.GetOrdinal("email_confirmado")),
            ObterDataOpcionalUtc(reader, "ultimo_login"),
            ObterDataUtc(reader, "created_at"),
            ObterDataUtc(reader, "updated_at"),
            ObterTextoOpcional(reader, "codigo_indicacao"));
    }

    private static string? ObterTextoOpcional(MySqlDataReader reader, string nomeColuna)
    {
        var ordinal = reader.GetOrdinal(nomeColuna);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? ObterDataOpcionalUtc(MySqlDataReader reader, string nomeColuna)
    {
        var ordinal = reader.GetOrdinal(nomeColuna);
        return reader.IsDBNull(ordinal) ? null : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }

    private static DateTime ObterDataUtc(MySqlDataReader reader, string nomeColuna) =>
        DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal(nomeColuna)), DateTimeKind.Utc);
}

using System.Data;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class IndicacaoMySqlRepository : IIndicacaoRepository
{
    private const string Colunas = """
        id,
        usuario_indicador_id,
        usuario_indicado_id,
        nome_indicada,
        telefone_indicada,
        codigo_indicacao_usado,
        vistoria_id,
        status,
        created_at,
        updated_at
        """;

    private readonly MySqlConnectionFactory _connectionFactory;

    public IndicacaoMySqlRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Indicacao?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM indicacoes WHERE id = @id;";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        AdicionarGuid(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? Materializar(reader)
            : null;
    }

    public async Task<IReadOnlyCollection<Indicacao>> ObterTodasAsync(CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM indicacoes ORDER BY created_at;";
        return await ObterColecaoAsync(sql, null, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Indicacao>> ObterPorUsuarioIndicadorIdAsync(
        Guid usuarioIndicadorId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM indicacoes WHERE usuario_indicador_id = @usuarioIndicadorId ORDER BY created_at;";
        return await ObterColecaoAsync(
            sql,
            command => AdicionarGuid(command, "@usuarioIndicadorId", usuarioIndicadorId),
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<Indicacao>> ObterPorStatusAsync(
        StatusIndicacao status,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM indicacoes WHERE status = @status ORDER BY created_at;";
        return await ObterColecaoAsync(
            sql,
            command => command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)status,
            cancellationToken);
    }

    public async Task AdicionarAsync(Indicacao indicacao, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indicacao);

        const string sql = """
            INSERT INTO indicacoes (
                id,
                usuario_indicador_id,
                usuario_indicado_id,
                nome_indicada,
                telefone_indicada,
                codigo_indicacao_usado,
                vistoria_id,
                status,
                created_at,
                updated_at)
            VALUES (
                @id,
                @usuarioIndicadorId,
                @usuarioIndicadoId,
                @nomeIndicada,
                @telefoneIndicada,
                @codigoIndicacaoUsado,
                @vistoriaId,
                @status,
                @createdAt,
                @updatedAt);
            """;

        await ExecutarComandoAsync(sql, command => AdicionarParametrosEstado(command, indicacao), cancellationToken);
    }

    public async Task AtualizarAsync(Indicacao indicacao, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indicacao);

        const string sql = """
            UPDATE indicacoes
            SET
                usuario_indicado_id = @usuarioIndicadoId,
                vistoria_id = @vistoriaId,
                status = @status,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        await ExecutarComandoAsync(sql, command =>
        {
            AdicionarGuid(command, "@id", indicacao.Id);
            AdicionarGuidOpcional(command, "@usuarioIndicadoId", indicacao.UsuarioIndicadoId);
            AdicionarGuidOpcional(command, "@vistoriaId", indicacao.VistoriaId);
            command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)indicacao.Status;
            command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = indicacao.UpdatedAt;
        }, cancellationToken);
    }

    private async Task<IReadOnlyCollection<Indicacao>> ObterColecaoAsync(
        string sql,
        Action<MySqlCommand>? adicionarParametros,
        CancellationToken cancellationToken)
    {
        var indicacoes = new List<Indicacao>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        adicionarParametros?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            indicacoes.Add(Materializar(reader));
        }

        return indicacoes.AsReadOnly();
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

    private static void AdicionarParametrosEstado(MySqlCommand command, Indicacao indicacao)
    {
        AdicionarGuid(command, "@id", indicacao.Id);
        AdicionarGuid(command, "@usuarioIndicadorId", indicacao.UsuarioIndicadorId);
        AdicionarGuidOpcional(command, "@usuarioIndicadoId", indicacao.UsuarioIndicadoId);
        command.Parameters.Add("@nomeIndicada", MySqlDbType.VarChar).Value = indicacao.NomeIndicada;
        command.Parameters.Add("@telefoneIndicada", MySqlDbType.VarChar).Value = indicacao.TelefoneIndicada;
        command.Parameters.Add("@codigoIndicacaoUsado", MySqlDbType.VarChar).Value = indicacao.CodigoIndicacaoUsado;
        AdicionarGuidOpcional(command, "@vistoriaId", indicacao.VistoriaId);
        command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)indicacao.Status;
        command.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = indicacao.CreatedAt;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = indicacao.UpdatedAt;
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private static void AdicionarGuidOpcional(MySqlCommand command, string nome, Guid? valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = (object?)valor?.ToString() ?? DBNull.Value;

    private static Indicacao Materializar(MySqlDataReader reader)
    {
        var statusPersistido = reader.GetInt32(reader.GetOrdinal("status"));
        if (!Enum.IsDefined(typeof(StatusIndicacao), statusPersistido))
            throw new DataException($"O status persistido '{statusPersistido}' é inválido.");

        return Indicacao.Reidratar(
            reader.ObterGuid("id"),
            reader.ObterGuid("usuario_indicador_id"),
            reader.ObterGuidOpcional("usuario_indicado_id"),
            reader.GetString(reader.GetOrdinal("nome_indicada")),
            reader.GetString(reader.GetOrdinal("telefone_indicada")),
            reader.GetString(reader.GetOrdinal("codigo_indicacao_usado")),
            reader.ObterGuidOpcional("vistoria_id"),
            (StatusIndicacao)statusPersistido,
            DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("created_at")), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("updated_at")), DateTimeKind.Utc));
    }

}

using System.Data;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class VistoriaMySqlRepository : IVistoriaRepository
{
    private const string Colunas = """
        id,
        usuario_id,
        tipo_planta,
        area_m2,
        pacote,
        data_agendada,
        status,
        created_at,
        updated_at
        """;

    private readonly MySqlConnectionFactory _connectionFactory;

    public VistoriaMySqlRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Vistoria?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM vistorias WHERE id = @id;";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        AdicionarGuid(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? Materializar(reader)
            : null;
    }

    public async Task<IReadOnlyCollection<Vistoria>> ObterTodasAsync(CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM vistorias ORDER BY created_at;";
        return await ObterColecaoAsync(sql, null, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Vistoria>> ObterPorUsuarioIdAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM vistorias WHERE usuario_id = @usuarioId ORDER BY created_at;";
        return await ObterColecaoAsync(
            sql,
            command => AdicionarGuid(command, "@usuarioId", usuarioId),
            cancellationToken);
    }

    public async Task AdicionarAsync(Vistoria vistoria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vistoria);

        const string sql = """
            INSERT INTO vistorias (
                id,
                usuario_id,
                tipo_planta,
                area_m2,
                pacote,
                data_agendada,
                status,
                created_at,
                updated_at)
            VALUES (
                @id,
                @usuarioId,
                @tipoPlanta,
                @areaM2,
                @pacote,
                @dataAgendada,
                @status,
                @createdAt,
                @updatedAt);
            """;

        await ExecutarComandoAsync(sql, command => AdicionarParametrosEstado(command, vistoria), cancellationToken);
    }

    public async Task AtualizarAsync(Vistoria vistoria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vistoria);

        const string sql = """
            UPDATE vistorias
            SET
                status = @status,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        await ExecutarComandoAsync(sql, command =>
        {
            AdicionarGuid(command, "@id", vistoria.Id);
            command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)vistoria.Status;
            command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = vistoria.UpdatedAt;
        }, cancellationToken);
    }

    private async Task<IReadOnlyCollection<Vistoria>> ObterColecaoAsync(
        string sql,
        Action<MySqlCommand>? adicionarParametros,
        CancellationToken cancellationToken)
    {
        var vistorias = new List<Vistoria>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        adicionarParametros?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            vistorias.Add(Materializar(reader));
        }

        return vistorias.AsReadOnly();
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

    private static void AdicionarParametrosEstado(MySqlCommand command, Vistoria vistoria)
    {
        AdicionarGuid(command, "@id", vistoria.Id);
        AdicionarGuid(command, "@usuarioId", vistoria.UsuarioId);
        command.Parameters.Add("@tipoPlanta", MySqlDbType.VarChar).Value = vistoria.TipoPlanta;
        command.Parameters.Add("@areaM2", MySqlDbType.Decimal).Value = vistoria.AreaM2;
        command.Parameters.Add("@pacote", MySqlDbType.Int32).Value = (int)vistoria.Pacote;
        command.Parameters.Add("@dataAgendada", MySqlDbType.DateTime).Value = vistoria.DataAgendada;
        command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)vistoria.Status;
        command.Parameters.Add("@createdAt", MySqlDbType.DateTime).Value = vistoria.CreatedAt;
        command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = vistoria.UpdatedAt;
    }

    private static void AdicionarGuid(MySqlCommand command, string nome, Guid valor) =>
        command.Parameters.Add(nome, MySqlDbType.VarChar).Value = valor.ToString();

    private static Vistoria Materializar(MySqlDataReader reader)
    {
        var pacotePersistido = reader.GetInt32(reader.GetOrdinal("pacote"));
        if (!Enum.IsDefined(typeof(PacoteVistoria), pacotePersistido))
            throw new DataException($"O pacote persistido '{pacotePersistido}' é inválido.");

        var statusPersistido = reader.GetInt32(reader.GetOrdinal("status"));
        if (!Enum.IsDefined(typeof(StatusVistoria), statusPersistido))
            throw new DataException($"O status persistido '{statusPersistido}' é inválido.");

        return Vistoria.Reidratar(
            Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            Guid.Parse(reader.GetString(reader.GetOrdinal("usuario_id"))),
            reader.GetString(reader.GetOrdinal("tipo_planta")),
            reader.GetDecimal(reader.GetOrdinal("area_m2")),
            (PacoteVistoria)pacotePersistido,
            reader.GetDateTime(reader.GetOrdinal("data_agendada")),
            (StatusVistoria)statusPersistido,
            ObterDataUtc(reader, "created_at"),
            ObterDataUtc(reader, "updated_at"));
    }

    private static DateTime ObterDataUtc(MySqlDataReader reader, string nomeColuna) =>
        DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal(nomeColuna)), DateTimeKind.Utc);
}

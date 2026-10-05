using System.Data;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class IndicacaoMySqlRepository : IIndicacaoRepository
{
    private const string ConstraintVistoriaVinculada = "uq_indicacoes_vistoria_id";

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

    public async Task<Indicacao?> ObterPorVistoriaIdAsync(
        Guid vistoriaId,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {Colunas} FROM indicacoes WHERE vistoria_id = @vistoriaId;";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        AdicionarGuid(command, "@vistoriaId", vistoriaId);
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

        try
        {
            await ExecutarComandoAsync(sql, command =>
            {
                AdicionarGuid(command, "@id", indicacao.Id);
                AdicionarGuidOpcional(command, "@usuarioIndicadoId", indicacao.UsuarioIndicadoId);
                AdicionarGuidOpcional(command, "@vistoriaId", indicacao.VistoriaId);
                command.Parameters.Add("@status", MySqlDbType.Int32).Value = (int)indicacao.Status;
                command.Parameters.Add("@updatedAt", MySqlDbType.DateTime).Value = indicacao.UpdatedAt;
            }, cancellationToken);
        }
        catch (MySqlException exception) when (EhVistoriaVinculadaEmOutraIndicacao(exception))
        {
            throw new VistoriaJaVinculadaOutraIndicacaoException();
        }
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
        await using var transaction=await connection.BeginTransactionAsync(cancellationToken);
        await using var command = CriarComando(connection, sql);
        command.Transaction=transaction;
        adicionarParametros(command);
        if(sql.TrimStart().StartsWith("UPDATE",StringComparison.Ordinal))
        {
            await using var anterior=new MySqlCommand("SELECT status FROM indicacoes WHERE id=@id FOR UPDATE",connection,transaction);
            anterior.Parameters.AddWithValue("@id",command.Parameters["@id"].Value);
            var estado=await anterior.ExecuteScalarAsync(cancellationToken);
            if(estado is not null and not DBNull && (Convert.ToInt32(estado) is 2 or 4 || (Convert.ToInt32(estado)==3 && Convert.ToInt32(command.Parameters["@status"].Value)!=3)))
                throw new DomainException("Indicação concluída não admite atualização independente da jornada financeira.");
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
        if(command.Parameters.Contains("@status") && Convert.ToInt32(command.Parameters["@status"].Value)==1)
        {
            var id=Guid.Parse(command.Parameters["@id"].Value?.ToString() ?? throw new InvalidOperationException("Identidade ausente."));
            using var destinatario=new MySqlCommand("SELECT usuario_indicador_id,vistoria_id FROM indicacoes WHERE id=@id",connection,transaction);
            destinatario.Parameters.AddWithValue("@id",id.ToString());
            Guid usuario;
            Guid vistoriaId;
            await using (var reader = await destinatario.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Indicação ausente.");
                usuario = reader.ObterGuid("usuario_indicador_id");
                vistoriaId = reader.ObterGuid("vistoria_id");
            }
            await NotificacoesNaTransacao.Criar(connection,transaction,Application.Jornada.TipoNotificacao.VistoriaVinculada,vistoriaId,usuario,cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
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

    private static bool EhVistoriaVinculadaEmOutraIndicacao(MySqlException exception) =>
        exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry &&
        exception.Message.Contains(ConstraintVistoriaVinculada, StringComparison.OrdinalIgnoreCase);

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

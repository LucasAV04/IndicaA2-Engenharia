using System.Data;
using Application.DTOs.Precificacao;
using Application.Interfaces.Stores;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Services;
using Infrastructure.Database;
using MySqlConnector;

namespace Infrastructure.Repositories;

internal enum PontoTransacionalPrecificacao { TipoBloqueado, PrecoDesativado, VistoriaInserida, SnapshotGravado }

public sealed class PrecificacaoMySqlStore : IPrecificacaoStore
{
    private readonly MySqlConnectionFactory _factory;
    private readonly Func<PontoTransacionalPrecificacao, MySqlConnection, MySqlTransaction, CancellationToken, Task> _ponto;
    private const string ColunasPreco = "id, tipo_planta_id, nome_tipo_planta, preco_m2, modalidade, acrescimo, versao, ativo, created_at, updated_at, desativado_em";

    public PrecificacaoMySqlStore(MySqlConnectionFactory factory) : this(factory, (_, _, _, _) => Task.CompletedTask) { }
    internal PrecificacaoMySqlStore(MySqlConnectionFactory factory,
        Func<PontoTransacionalPrecificacao, MySqlConnection, MySqlTransaction, CancellationToken, Task> ponto)
    { _factory = factory; _ponto = ponto; }

    public async Task<IReadOnlyCollection<TipoPlantaResponseDto>> ListarTiposAsync(CancellationToken token)
    {
        await using var c = _factory.Create(); await c.OpenAsync(token);
        await using var cmd = new MySqlCommand("""
            SELECT t.id, t.nome, t.ativo, t.created_at, t.updated_at,
                EXISTS(SELECT 1 FROM precos_vistoria p WHERE p.tipo_ativo=t.id) AS possui_preco
            FROM tipos_planta t ORDER BY t.nome, t.id;
            """, c);
        await using var r = await cmd.ExecuteReaderAsync(token);
        var result = new List<TipoPlantaResponseDto>();
        while (await r.ReadAsync(token)) result.Add(new(r.ObterGuid("id"), r.GetString("nome"), r.GetBoolean("ativo"),
            r.GetBoolean("possui_preco"), Utc(r, "created_at"), Utc(r, "updated_at")));
        return result;
    }

    public async Task CriarTipoAsync(TipoPlanta tipo, CancellationToken token)
    {
        await using var c = _factory.Create(); await c.OpenAsync(token);
        await using var cmd = new MySqlCommand("INSERT INTO tipos_planta (id,nome,nome_normalizado,ativo,created_at,updated_at) VALUES (@id,@nome,@normalizado,1,@instante,@instante);", c);
        P(cmd, "@id", tipo.Id); P(cmd, "@nome", tipo.Nome); P(cmd, "@normalizado", tipo.NomeNormalizado); P(cmd, "@instante", tipo.CreatedAt);
        try { await cmd.ExecuteNonQueryAsync(token); }
        catch (MySqlException ex) when (NomeDuplicado(ex)) { throw new PrecificacaoException("nome_duplicado"); }
    }

    public async Task RenomearTipoAsync(Guid id, string nome, DateTime instante, CancellationToken token)
    {
        await Transacao(async (c, tx) =>
        {
            var tipo = await TipoBloqueado(c, tx, id, token);
            tipo.Renomear(nome, instante);
            await using var cmd = new MySqlCommand("UPDATE tipos_planta SET nome=@nome,nome_normalizado=@normalizado,updated_at=@instante WHERE id=@id;", c, tx);
            P(cmd, "@id", id); P(cmd, "@nome", tipo.Nome); P(cmd, "@normalizado", tipo.NomeNormalizado); P(cmd, "@instante", instante);
            try { await cmd.ExecuteNonQueryAsync(token); }
            catch (MySqlException ex) when (NomeDuplicado(ex)) { throw new PrecificacaoException("nome_duplicado"); }
            return true;
        }, token);
    }

    public async Task DesativarTipoAsync(Guid id, DateTime instante, CancellationToken token)
    {
        await Transacao(async (c, tx) =>
        {
            var tipo = await TipoBloqueado(c, tx, id, token);
            if (await Ativo(c, tx, id, token) is not null) throw new PrecificacaoException("desative_preco_primeiro");
            tipo.Desativar(instante);
            await using var cmd = new MySqlCommand("UPDATE tipos_planta SET ativo=0,updated_at=@instante WHERE id=@id;", c, tx);
            P(cmd, "@id", id); P(cmd, "@instante", tipo.UpdatedAt);
            await cmd.ExecuteNonQueryAsync(token);
            return true;
        }, token);
    }

    public Task<IReadOnlyCollection<PrecoVistoria>> ListarAtivosAsync(CancellationToken token) =>
        LerPrecos($"SELECT {ColunasPreco} FROM precos_vistoria WHERE ativo=1 ORDER BY nome_tipo_planta,id;", null, token);
    public Task<IReadOnlyCollection<PrecoVistoria>> HistoricoAsync(Guid tipoId, CancellationToken token) =>
        LerPrecos($"SELECT {ColunasPreco} FROM precos_vistoria WHERE tipo_planta_id=@tipo ORDER BY versao DESC;", tipoId, token);

    public Task<PrecoVistoria> PublicarAsync(Guid tipoId, PublicarPrecoDto entrada, DateTime instante, CancellationToken token) =>
        Transacao(async (c, tx) =>
        {
            var tipo = await TipoBloqueado(c, tx, tipoId, token);
            if (!tipo.Ativo) throw new PrecificacaoException("tipo_inativo");
            int ultima;
            await using (var cmd = new MySqlCommand("SELECT COALESCE(MAX(versao),0) FROM precos_vistoria WHERE tipo_planta_id=@tipo;", c, tx))
            { P(cmd, "@tipo", tipoId); ultima = Convert.ToInt32(await cmd.ExecuteScalarAsync(token)); }
            if (ultima != entrada.VersaoEsperada) throw new PrecificacaoException("versao_conflitante");
            var preco = new PrecoVistoria(tipo.Id, tipo.Nome, entrada.PrecoM2, entrada.Modalidade, entrada.Acrescimo, checked(ultima + 1), instante);
            await DesativarAtual(c, tx, tipoId, instante, token);
            await _ponto(PontoTransacionalPrecificacao.PrecoDesativado, c, tx, token);
            await using var insert = new MySqlCommand("""
                INSERT INTO precos_vistoria (id,tipo_planta_id,nome_tipo_planta,preco_m2,modalidade,acrescimo,versao,ativo,created_at,updated_at)
                VALUES (@id,@tipo,@nome,@preco,@modalidade,@acrescimo,@versao,1,@instante,@instante);
                """, c, tx);
            P(insert, "@id", preco.Id); P(insert, "@tipo", tipo.Id); P(insert, "@nome", tipo.Nome);
            P(insert, "@preco", preco.PrecoM2); P(insert, "@modalidade", (int)preco.Modalidade); P(insert, "@acrescimo", preco.Acrescimo);
            P(insert, "@versao", preco.Versao); P(insert, "@instante", instante);
            await insert.ExecuteNonQueryAsync(token);
            return preco;
        }, token);

    public async Task DesativarPrecoAsync(Guid tipoId, Guid precoId, DateTime instante, CancellationToken token)
    {
        await Transacao(async (c, tx) =>
        {
            await TipoBloqueado(c, tx, tipoId, token);
            var atual = await Ativo(c, tx, tipoId, token);
            if (atual?.Id != precoId) throw new PrecificacaoException("versao_conflitante");
            await DesativarAtual(c, tx, tipoId, instante, token);
            return true;
        }, token);
    }

    public async Task<CalculoVistoria> SimularAsync(SimularVistoriaDto entrada, DateTime instante, CancellationToken token)
    {
        await using var c = _factory.Create(); await c.OpenAsync(token);
        // Uma leitura consistente; não bloqueia linhas nem altera timestamps.
        await using var cmd = new MySqlCommand("""
            SELECT p.id,p.tipo_planta_id,p.nome_tipo_planta,p.preco_m2,p.modalidade,p.acrescimo,p.versao,p.ativo,
                p.created_at,p.updated_at,p.desativado_em,t.nome AS nome_atual
            FROM tipos_planta t JOIN precos_vistoria p ON p.tipo_ativo=t.id
            WHERE t.id=@tipo AND t.ativo=1;
            """, c);
        P(cmd, "@tipo", entrada.TipoPlantaId);
        await using var r = await cmd.ExecuteReaderAsync(token);
        if (!await r.ReadAsync(token)) throw new PrecificacaoException("preco_ausente");
        return MotorPrecificacaoVistoria.Calcular(Materializar(r), r.GetString("nome_atual"), entrada.AreaM2, entrada.Pacote, instante);
    }

    public Task<Vistoria> CriarVistoriaAsync(Guid usuarioId, Guid tipoId, decimal area, PacoteVistoria pacote,
        DateTime dataAgendada, DateTime instante, CancellationToken token) => Transacao(async (c, tx) =>
        {
            var tipo = await TipoBloqueado(c, tx, tipoId, token);
            if (!tipo.Ativo) throw new PrecificacaoException("tipo_inativo");
            var preco = await Ativo(c, tx, tipoId, token) ?? throw new PrecificacaoException("preco_ausente");
            var calculo = MotorPrecificacaoVistoria.Calcular(preco, tipo.Nome, area, pacote, instante);
            var vistoria = Vistoria.CriarCalculada(usuarioId, dataAgendada, calculo);
            await using (var cmd = new MySqlCommand("""
                INSERT INTO vistorias (id,usuario_id,tipo_planta,area_m2,pacote,data_agendada,status,created_at,updated_at)
                VALUES (@id,@usuario,@nome,@area,@pacote,@data,0,@instante,@instante);
                """, c, tx))
            {
                P(cmd, "@id", vistoria.Id); P(cmd, "@usuario", usuarioId); P(cmd, "@nome", tipo.Nome);
                P(cmd, "@area", area); P(cmd, "@pacote", (int)pacote); P(cmd, "@data", dataAgendada); P(cmd, "@instante", instante);
                await cmd.ExecuteNonQueryAsync(token);
            }
            await _ponto(PontoTransacionalPrecificacao.VistoriaInserida, c, tx, token);
            await using (var cmd = new MySqlCommand("""
                UPDATE vistorias SET tipo_planta_id=@tipo,preco_vistoria_id=@precoId,preco_versao=@versao,
                    preco_m2=@preco,preco_modalidade=@modalidade,preco_acrescimo=@acrescimo,
                    valor_base=@base,valor_final=@final,calculado_em=@instante WHERE id=@id;
                """, c, tx))
            {
                P(cmd, "@id", vistoria.Id); P(cmd, "@tipo", tipoId); P(cmd, "@precoId", preco.Id); P(cmd, "@versao", preco.Versao);
                P(cmd, "@preco", calculo.PrecoM2); P(cmd, "@modalidade", (int)calculo.Modalidade); P(cmd, "@acrescimo", calculo.Acrescimo);
                P(cmd, "@base", calculo.ValorBase); P(cmd, "@final", calculo.ValorFinal); P(cmd, "@instante", instante);
                if (await cmd.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Snapshot não persistido.");
            }
            await _ponto(PontoTransacionalPrecificacao.SnapshotGravado, c, tx, token);
            return vistoria;
        }, token);

    private async Task<T> Transacao<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> executar, CancellationToken token)
    {
        await using var c = _factory.Create(); await c.OpenAsync(token);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try { var result = await executar(c, tx); await tx.CommitAsync(token); return result; }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task<TipoPlanta> TipoBloqueado(MySqlConnection c, MySqlTransaction tx, Guid id, CancellationToken token)
    {
        TipoPlanta tipo;
        await using (var cmd = new MySqlCommand("SELECT id,nome,ativo,created_at,updated_at FROM tipos_planta WHERE id=@id FOR UPDATE;", c, tx))
        {
            P(cmd, "@id", id);
            await using var r = await cmd.ExecuteReaderAsync(token);
            if (!await r.ReadAsync(token)) throw new PrecificacaoException("tipo_ausente");
            tipo = TipoPlanta.Reidratar(id, r.GetString("nome"), r.GetBoolean("ativo"), Utc(r, "created_at"), Utc(r, "updated_at"));
        }
        await _ponto(PontoTransacionalPrecificacao.TipoBloqueado, c, tx, token);
        return tipo;
    }

    private static async Task<PrecoVistoria?> Ativo(MySqlConnection c, MySqlTransaction tx, Guid tipoId, CancellationToken token)
    {
        await using var cmd = new MySqlCommand($"SELECT {ColunasPreco} FROM precos_vistoria WHERE tipo_ativo=@tipo FOR UPDATE;", c, tx);
        P(cmd, "@tipo", tipoId); await using var r = await cmd.ExecuteReaderAsync(token);
        return await r.ReadAsync(token) ? Materializar(r) : null;
    }
    private static async Task DesativarAtual(MySqlConnection c, MySqlTransaction tx, Guid tipoId, DateTime instante, CancellationToken token)
    {
        await using var cmd = new MySqlCommand("UPDATE precos_vistoria SET ativo=0,desativado_em=@instante,updated_at=@instante WHERE tipo_ativo=@tipo;", c, tx);
        P(cmd, "@tipo", tipoId); P(cmd, "@instante", instante); await cmd.ExecuteNonQueryAsync(token);
    }
    private async Task<IReadOnlyCollection<PrecoVistoria>> LerPrecos(string sql, Guid? tipoId, CancellationToken token)
    {
        await using var c = _factory.Create(); await c.OpenAsync(token);
        await using var cmd = new MySqlCommand(sql, c); if (tipoId.HasValue) P(cmd, "@tipo", tipoId.Value);
        await using var r = await cmd.ExecuteReaderAsync(token); var result = new List<PrecoVistoria>();
        while (await r.ReadAsync(token)) result.Add(Materializar(r));
        return result;
    }
    private static PrecoVistoria Materializar(MySqlDataReader r) => PrecoVistoria.Reidratar(r.ObterGuid("id"), r.ObterGuid("tipo_planta_id"),
        r.GetString("nome_tipo_planta"), r.GetDecimal("preco_m2"), (ModalidadeAcrescimo)r.GetInt32("modalidade"), r.GetDecimal("acrescimo"),
        r.GetInt32("versao"), r.GetBoolean("ativo"), Utc(r, "created_at"), Utc(r, "updated_at"), r.IsDBNull(r.GetOrdinal("desativado_em")) ? null : Utc(r, "desativado_em"));
    private static DateTime Utc(MySqlDataReader r, string coluna) => DateTime.SpecifyKind(r.GetDateTime(coluna), DateTimeKind.Utc);
    private static void P(MySqlCommand cmd, string nome, object valor) => cmd.Parameters.AddWithValue(nome, valor is Guid id ? id.ToString() : valor);
    private static bool NomeDuplicado(MySqlException ex) => ex.Number == 1062 && ex.Message.Contains("uq_tipos_planta_nome", StringComparison.Ordinal);
}

using Application.Jornada;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;
using static Infrastructure.Repositories.CobrancaPixVistoriaMySqlStore;

namespace Infrastructure.Repositories;

public sealed class JornadaConsultaMySqlStore(MySqlConnectionFactory factory) : IJornadaConsultaStore
{
    public async Task<(string Nome,string Codigo)> PerfilAsync(Guid usuarioId,CancellationToken ct)
    {
        await using var c=factory.Create(); await c.OpenAsync(ct);
        using var cmd=Comando(c,null,"SELECT nome,codigo_indicacao FROM usuarios WHERE id=@id AND tipo_usuario=1 AND status=1",("id",usuarioId));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct) || r.IsDBNull(1)) throw new KeyNotFoundException("Perfil indisponível.");
        return(r.GetString(0),r.GetString(1));
    }
    public Task<IReadOnlyList<IndicacaoPortal>> IndicacoesAsync(Guid usuarioId,CancellationToken ct)=>LerLista(
        "SELECT nome_indicada,telefone_indicada,status,created_at FROM indicacoes WHERE usuario_indicador_id=@usuario ORDER BY created_at DESC,id",
        r=>new IndicacaoPortal(JornadaValidacao.PrimeiroNome(r.GetString(0)),JornadaValidacao.TelefoneMascarado(r.GetString(1)),(StatusIndicacao)r.GetInt32(2),Data(r,"created_at")!.Value),ct,("usuario",usuarioId));
    public Task<IReadOnlyList<CashbackPortal>> CashbacksAsync(Guid usuarioId,CancellationToken ct)=>LerLista(
        "SELECT c.valor,c.status,p.status pix_status,c.created_at FROM cashbacks c LEFT JOIN pagamentos_pix p ON p.cashback_id=c.id AND p.usuario_beneficiario_id=c.usuario_indicador_id WHERE c.usuario_indicador_id=@usuario ORDER BY c.created_at DESC,c.id",
        r=>new CashbackPortal(r.GetDecimal(0),(StatusCashback)r.GetInt32(1),r.IsDBNull(2)?null:(StatusPagamentoPix)r.GetInt32(2),Data(r,"created_at")!.Value),ct,("usuario",usuarioId));
    public Task<IReadOnlyList<NotificacaoResponse>> NotificacoesAsync(Guid usuarioId,bool administrador,CancellationToken ct)=>LerLista(
        "SELECT id,tipo,created_at,lida_em FROM notificacoes_internas WHERE escopo=@escopo AND (usuario_id=@usuario OR (@escopo=1 AND usuario_id IS NULL)) ORDER BY created_at DESC,id LIMIT 100",
        r=>new NotificacaoResponse(r.ObterGuid("id"),(TipoNotificacao)r.GetInt32("tipo"),Data(r,"created_at")!.Value,Data(r,"lida_em")),ct,("usuario",usuarioId),("escopo",administrador?1:0));
    public async Task<long> NaoLidasAsync(Guid usuarioId,bool administrador,CancellationToken ct)
    {
        await using var c=factory.Create(); await c.OpenAsync(ct);
        using var cmd=Comando(c,null,"SELECT COUNT(*) FROM notificacoes_internas WHERE lida_em IS NULL AND escopo=@escopo AND (usuario_id=@usuario OR (@escopo=1 AND usuario_id IS NULL))",("usuario",usuarioId),("escopo",administrador?1:0));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }
    public async Task<bool> LerAsync(Guid usuarioId,bool administrador,Guid? notificacaoId,DateTime agora,CancellationToken ct)
    {
        await using var c=factory.Create(); await c.OpenAsync(ct);
        using var cmd=Comando(c,null,"UPDATE notificacoes_internas SET lida_em=COALESCE(lida_em,@agora) WHERE (@id IS NULL OR id=@id) AND escopo=@escopo AND (usuario_id=@usuario OR (@escopo=1 AND usuario_id IS NULL))",("id",notificacaoId),("usuario",usuarioId),("escopo",administrador?1:0),("agora",agora));
        return await cmd.ExecuteNonQueryAsync(ct)>0 || notificacaoId is null;
    }
    public async Task<IndicadoresJornada> IndicadoresAsync(CancellationToken ct)
    {
        var rows=await LerLista("""
            SELECT
              (SELECT COUNT(*) FROM cashbacks c WHERE c.status=1 AND NOT EXISTS(SELECT 1 FROM pagamentos_pix p WHERE p.cashback_id=c.id) AND NOT EXISTS(SELECT 1 FROM dados_pix d WHERE d.usuario_id=c.usuario_indicador_id)) sem_dados,
              (SELECT COUNT(*) FROM cashbacks c WHERE c.status=1 AND NOT EXISTS(SELECT 1 FROM pagamentos_pix p WHERE p.cashback_id=c.id)) sem_ordem,
              (SELECT COUNT(*) FROM pagamentos_pix WHERE status=0) pendente,
              (SELECT COUNT(*) FROM pagamentos_pix WHERE status=1) processando,
              (SELECT COUNT(*) FROM pagamentos_pix WHERE status=2) concluido,
              (SELECT COUNT(*) FROM pagamentos_pix WHERE status=3) falhou,
              (SELECT COUNT(*) FROM pagamentos_pix WHERE status=4) definitiva,
              (SELECT COUNT(*) FROM notificacoes_internas WHERE escopo=1 AND lida_em IS NULL) nao_lidas
            """,r=>new IndicadoresJornada(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.GetInt64(3),r.GetInt64(4),r.GetInt64(5),r.GetInt64(6),r.GetInt64(7)),ct);
        return rows.Single();
    }
    public Task<IReadOnlyList<OrigemIndicacaoResponse>> OrigensAsync(CancellationToken ct)=>LerLista(
        "SELECT id,origem,consentimento_versao,consentimento_em FROM indicacoes ORDER BY created_at DESC,id",
        r=>new OrigemIndicacaoResponse(r.ObterGuid("id"),r.GetInt32("origem"),r.IsDBNull(2)?null:r.GetString(2),Data(r,"consentimento_em")),ct);
    private async Task<IReadOnlyList<T>> LerLista<T>(string sql,Func<MySqlDataReader,T> map,CancellationToken ct,params (string Nome,object? Valor)[] args)
    {
        await using var c=factory.Create(); await c.OpenAsync(ct); using var cmd=Comando(c,null,sql,args);
        await using var r=await cmd.ExecuteReaderAsync(ct); var rows=new List<T>();
        while(await r.ReadAsync(ct)) rows.Add(map(r)); return rows;
    }
}

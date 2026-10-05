using Application.Jornada;
using MySqlConnector;
using static Infrastructure.Repositories.CobrancaPixVistoriaMySqlStore;

namespace Infrastructure.Repositories;

internal static class NotificacoesNaTransacao
{
    internal static async Task PagamentoConfirmado(MySqlConnection c,MySqlTransaction t,Guid pagamentoId,CancellationToken ct)
    {
        using var cmd=Comando(c,t,"""
            SELECT v.usuario_id FROM pagamentos_vistoria p JOIN vistorias v ON v.id=p.vistoria_id WHERE p.id=@id
            UNION SELECT i.usuario_indicador_id FROM pagamentos_vistoria p JOIN indicacoes i ON i.vistoria_id=p.vistoria_id WHERE p.id=@id
            """,("id",pagamentoId));
        var ids=new List<Guid>();
        await using(var r=await cmd.ExecuteReaderAsync(ct))
            while(await r.ReadAsync(ct)) ids.Add(Infrastructure.Database.MySqlDataReaderExtensions.ObterGuid(r,"usuario_id"));
        foreach(var id in ids) await Criar(c,t,TipoNotificacao.PagamentoConfirmado,pagamentoId,id,ct);
    }
    internal static async Task BloquearIndicacaoDoPix(MySqlConnection c,MySqlTransaction t,Guid pixId,CancellationToken ct)
    {
        using var identidade=Comando(c,t,"SELECT c.indicacao_id FROM pagamentos_pix p JOIN cashbacks c ON c.id=p.cashback_id WHERE p.id=@id",("id",pixId));
        var id=await identidade.ExecuteScalarAsync(ct);
        if(id is null or DBNull) return;
        using var bloquear=Comando(c,t,"SELECT id FROM indicacoes WHERE id=@id FOR UPDATE",("id",id));
        await bloquear.ExecuteScalarAsync(ct);
    }

    internal static async Task Liquidacao(MySqlConnection c,MySqlTransaction t,Domain.Entities.Cashback cashback,bool confirmado,CancellationToken ct,
        Infrastructure.Database.InterceptadorTransacionalPix interceptar)
    {
        Guid indicadaId;
        using var ler=Comando(c,t,"""
            SELECT i.status,i.usuario_indicador_id,i.usuario_indicado_id,v.usuario_id,v.status vistoria_status,v.valor_final,p.status pagamento_status,p.valor,p.pago_em
            FROM indicacoes i JOIN vistorias v ON v.id=i.vistoria_id JOIN pagamentos_vistoria p ON p.vistoria_id=v.id
            WHERE i.id=@indicacao AND p.id=@pagamento
            """,("indicacao",cashback.IndicacaoId),("pagamento",cashback.PagamentoVistoriaId));
        await using(var r=await ler.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct) || r.GetInt32("status") is not (2 or 4)
                || (!confirmado && r.GetInt32("status")==4)
                || r.GetInt32("vistoria_status")!=2 || r.GetInt32("pagamento_status")!=1 || r.IsDBNull(r.GetOrdinal("pago_em"))
                || r.IsDBNull(r.GetOrdinal("valor_final")) || r.GetDecimal("valor_final")!=cashback.ValorTotalPago || r.GetDecimal("valor")!=cashback.ValorTotalPago
                || Infrastructure.Database.MySqlDataReaderExtensions.ObterGuid(r,"usuario_indicador_id")!=cashback.UsuarioIndicadorId
                || Infrastructure.Database.MySqlDataReaderExtensions.ObterGuidOpcional(r,"usuario_indicado_id")!=Infrastructure.Database.MySqlDataReaderExtensions.ObterGuid(r,"usuario_id")
                || cashback.Percentual!=0.20m || cashback.Valor!=decimal.Round(cashback.ValorTotalPago*0.20m,2,MidpointRounding.AwayFromZero))
                throw new InvalidOperationException("Jornada financeira incompatível com liquidação.");
            indicadaId=Infrastructure.Database.MySqlDataReaderExtensions.ObterGuid(r,"usuario_id");
        }
        if(confirmado)
        {
            using var update=Comando(c,t,"UPDATE indicacoes SET status=4,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status=2",("id",cashback.IndicacaoId));
            await update.ExecuteNonQueryAsync(ct);
            await Criar(c,t,TipoNotificacao.CashbackPago,cashback.Id,cashback.UsuarioIndicadorId,ct);
            await interceptar(Infrastructure.Database.PontoTransacionalPix.AntesDeNotificarClienteIndicada,c,t,ct);
            await Criar(c,t,TipoNotificacao.CashbackPago,cashback.Id,indicadaId,ct);
            await Criar(c,t,TipoNotificacao.CashbackPago,cashback.Id,null,ct);
        }
        else await Criar(c,t,TipoNotificacao.FalhaFinanceira,cashback.Id,null,ct);
    }

    internal static async Task Criar(MySqlConnection c,MySqlTransaction t,TipoNotificacao tipo,Guid referencia,Guid? usuario,CancellationToken ct)
    {
        var chave=$"{(int)tipo}:{referencia:N}:{(usuario.HasValue ? usuario.Value.ToString("N") : "admin")}";
        using var cmd=Comando(c,t,"""
            INSERT INTO notificacoes_internas(id,tipo,escopo,usuario_id,referencia_id,evento_chave,created_at)
            VALUES(@id,@tipo,@escopo,@usuario,@ref,@chave,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE evento_chave=evento_chave
            """,("id",Guid.NewGuid()),("tipo",(int)tipo),("escopo",usuario.HasValue?0:1),("usuario",usuario),("ref",referencia),("chave",chave));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

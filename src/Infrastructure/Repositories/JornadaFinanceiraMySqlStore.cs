using Application.Jornada;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Infrastructure.Database;
using Infrastructure.Security;
using MySqlConnector;
using static Infrastructure.Repositories.CobrancaPixVistoriaMySqlStore;

namespace Infrastructure.Repositories;

public sealed class JornadaFinanceiraMySqlStore(MySqlConnectionFactory factory,Func<IDadosPixProtector> obterProtector) : IJornadaFinanceiraStore
{
    public JornadaFinanceiraMySqlStore(MySqlConnectionFactory factory,IDadosPixProtector protector):this(factory,()=>protector) { }
    internal Func<string,MySqlConnection,MySqlTransaction,Task> Interceptar { get; init; }=(_,_,_)=>Task.CompletedTask;

    public async Task ConcluirVistoriaAsync(Guid vistoriaId,CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(vistoriaId,Guid.Empty);
        await using var c=factory.Create(); await c.OpenAsync(ct); await using var t=await c.BeginTransactionAsync(ct);
        using var vistoria=Comando(c,t,"SELECT usuario_id,status,valor_final,preco_vistoria_id FROM vistorias WHERE id=@id FOR UPDATE",("id",vistoriaId));
        Guid proprietario; int estado; decimal? total;
        await using(var r=await vistoria.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct)) throw new VistoriaNaoEncontradaException();
            proprietario=r.ObterGuid("usuario_id"); estado=r.GetInt32("status"); total=r.IsDBNull(2)?null:r.GetDecimal(2);
        }
        // Uma repetição não altera a cadeia histórica nem tenta recuperar ordens.
        if(estado==2) { await t.CommitAsync(ct); return; }
        if(estado!=1) throw new DomainException("Somente uma vistoria realizada pode ser concluída.");
        using var indicacao=Comando(c,t,"SELECT id,usuario_indicador_id,usuario_indicado_id,status FROM indicacoes WHERE vistoria_id=@id FOR UPDATE",("id",vistoriaId));
        Guid? indicacaoId=null; Guid indicador=default; int indicacaoStatus=0;
        await using(var r=await indicacao.ExecuteReaderAsync(ct))
        {
            if(await r.ReadAsync(ct))
            {
                indicacaoId=r.ObterGuid("id"); indicador=r.ObterGuid("usuario_indicador_id"); indicacaoStatus=r.GetInt32("status");
                if(r.ObterGuidOpcional("usuario_indicado_id")!=proprietario || indicador==proprietario) throw new DomainException("Vínculo da indicação incompatível.");
            }
        }
        using var pagamento=Comando(c,t,"SELECT id,valor,status,pago_em FROM pagamentos_vistoria WHERE vistoria_id=@id FOR UPDATE",("id",vistoriaId));
        Guid pagamentoId; decimal valor;
        await using(var r=await pagamento.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct) || r.GetInt32("status")!=1 || r.IsDBNull(3)) throw new DomainException("Pagamento confirmado obrigatório para concluir.");
            pagamentoId=r.ObterGuid("id"); valor=r.GetDecimal("valor");
        }
        if(total.HasValue && total.Value!=valor) throw new DomainException("Pagamento incompatível com o snapshot da vistoria.");
        if(indicacaoId.HasValue && !total.HasValue) throw new DomainException("Vistoria legada exige regularização financeira antes do cashback.");
        using(var divergencia=Comando(c,t,"SELECT COUNT(*) FROM cobrancas_pix_vistoria WHERE pagamento_vistoria_id=@id AND (status=10 OR codigo IN ('dados-divergentes','evidencia-divergente','bloqueio-operacional'))",("id",pagamentoId)))
            if(Convert.ToInt64(await divergencia.ExecuteScalarAsync(ct))!=0) throw new DomainException("Pagamento com pendência financeira.");
        using(var update=Comando(c,t,"UPDATE vistorias SET status=2,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status=1",("id",vistoriaId)))
            if(await update.ExecuteNonQueryAsync(ct)!=1) throw new InvalidOperationException("Conclusão concorrente incompatível.");
        await Interceptar("Vistoria",c,t);
        if(indicacaoId.HasValue && indicacaoStatus!=3)
        {
            if(indicacaoStatus!=1) throw new DomainException("Indicação incompatível com conclusão.");
            using(var update=Comando(c,t,"UPDATE indicacoes SET status=2,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status=1",("id",indicacaoId)))
                if(await update.ExecuteNonQueryAsync(ct)!=1) throw new InvalidOperationException("Indicação concorrente incompatível.");
            await Interceptar("Indicacao",c,t);
            var calculado=Cashback.Criar(indicacaoId.Value,pagamentoId,indicador,valor);
            if(calculado.Valor<=0) throw new DomainException("Cashback fora da capacidade monetária suportada.");
            var cashback=await ObterCashback(c,t,pagamentoId,ct);
            if(cashback is null)
            {
                using var insert=Comando(c,t,"""
                    INSERT INTO cashbacks(id,indicacao_id,pagamento_vistoria_id,usuario_indicador_id,valor_total_pago,percentual,valor,status,created_at,updated_at)
                    VALUES(@id,@indicacao,@pagamento,@usuario,@base,@percentual,@valor,0,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
                    """,("id",calculado.Id),("indicacao",indicacaoId),("pagamento",pagamentoId),("usuario",indicador),("base",valor),("percentual",calculado.Percentual),("valor",calculado.Valor));
                await insert.ExecuteNonQueryAsync(ct); cashback=calculado;
            }
            if(cashback.IndicacaoId!=indicacaoId || cashback.UsuarioIndicadorId!=indicador || cashback.ValorTotalPago!=valor || cashback.Percentual!=calculado.Percentual || cashback.Valor!=calculado.Valor || cashback.Status is not (StatusCashback.Pendente or StatusCashback.Disponivel))
                throw new DomainException("Snapshot de cashback incompatível.");
            await Interceptar("Cashback",c,t);
            using(var aprovar=Comando(c,t,"UPDATE cashbacks SET status=1,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status=0",("id",cashback.Id))) await aprovar.ExecuteNonQueryAsync(ct);
            await Interceptar("Aprovacao",c,t);
            await NotificacoesNaTransacao.Criar(c,t,TipoNotificacao.CashbackCriado,cashback.Id,indicador,ct);
            await PrepararNaTransacao(c,t,cashback.Id,indicador,cashback.Valor,ct);
            await Interceptar("Notificacao",c,t);
        }
        ct.ThrowIfCancellationRequested(); await t.CommitAsync(ct);
    }

    public async Task PrepararCashbackAsync(Guid cashbackId,CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(cashbackId,Guid.Empty);
        await using var c=factory.Create(); await c.OpenAsync(ct); await using var t=await c.BeginTransactionAsync(ct);
        // Prefixo comum com a conclusão e a aplicação: indicação antes do cashback.
        using(var identidade=Comando(c,t,"SELECT indicacao_id FROM cashbacks WHERE id=@id",("id",cashbackId)))
        {
            var indId=await identidade.ExecuteScalarAsync(ct); if(indId is null or DBNull) return;
            using var ind=Comando(c,t,"SELECT id FROM indicacoes WHERE id=@id FOR UPDATE",("id",indId));
            if(await ind.ExecuteScalarAsync(ct) is null) return;
        }
        using var cmd=Comando(c,t,"SELECT * FROM cashbacks WHERE id=@id FOR UPDATE",("id",cashbackId));
        Cashback cashback;
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        { if(!await r.ReadAsync(ct)) return; cashback=CashbackMySqlRepository.Materializar(r); }
        if(cashback.Status!=StatusCashback.Disponivel) return;
        using(var coerencia=Comando(c,t,"""
            SELECT COUNT(*) FROM indicacoes i JOIN vistorias v ON v.id=i.vistoria_id JOIN pagamentos_vistoria p ON p.vistoria_id=v.id
            WHERE i.id=@indicacao AND i.status=2 AND i.usuario_indicador_id=@usuario AND i.usuario_indicado_id=v.usuario_id
              AND v.status=2 AND p.id=@pagamento AND p.status=1 AND p.valor=@base AND p.pago_em IS NOT NULL AND v.valor_final=p.valor
            """,("indicacao",cashback.IndicacaoId),("usuario",cashback.UsuarioIndicadorId),("pagamento",cashback.PagamentoVistoriaId),("base",cashback.ValorTotalPago)))
            if(Convert.ToInt64(await coerencia.ExecuteScalarAsync(ct))!=1 || cashback.Percentual!=0.20m || cashback.Valor!=decimal.Round(cashback.ValorTotalPago*0.20m,2,MidpointRounding.AwayFromZero))
                throw new DomainException("Cashback sem jornada financeira coerente.");
        await PrepararNaTransacao(c,t,cashback.Id,cashback.UsuarioIndicadorId,cashback.Valor,ct);
        ct.ThrowIfCancellationRequested(); await t.CommitAsync(ct);
    }

    private async Task PrepararNaTransacao(MySqlConnection c,MySqlTransaction t,Guid cashbackId,Guid usuarioId,decimal valor,CancellationToken ct)
    {
        using(var existente=Comando(c,t,"SELECT usuario_beneficiario_id,valor FROM pagamentos_pix WHERE cashback_id=@id FOR UPDATE",("id",cashbackId)))
        {
            await using var r=await existente.ExecuteReaderAsync(ct);
            if(await r.ReadAsync(ct))
            {
                if(r.ObterGuid("usuario_beneficiario_id")!=usuarioId || r.GetDecimal("valor")!=valor)
                    throw new DomainException("Ordem existente incompatível com o cashback.");
                return;
            }
        }
        using var cmd=Comando(c,t,"SELECT * FROM dados_pix WHERE usuario_id=@id FOR SHARE",("id",usuarioId));
        DadosPix? dados=null;
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        {
            if(await r.ReadAsync(ct))
            {
                var material=new DadosPixProtegido((byte[])r["chave_pix_ciphertext"],(byte[])r["chave_pix_nonce"],(byte[])r["chave_pix_tag"],r.GetInt32("encryption_version"));
                dados=new DadosPix(usuarioId,(TipoChavePix)r.GetInt32("tipo_chave_pix"),obterProtector().Desproteger(material));
            }
        }
        if(dados is null)
        {
            await NotificacoesNaTransacao.Criar(c,t,TipoNotificacao.DadosPixNecessarios,cashbackId,usuarioId,ct);
            await NotificacoesNaTransacao.Criar(c,t,TipoNotificacao.DadosPixNecessarios,cashbackId,null,ct);
            return;
        }
        var pix=PagamentoPix.Criar(cashbackId,usuarioId,valor,dados.TipoChavePix,dados.ChavePix);
        var protegido=obterProtector().Proteger(pix.ChavePix,PagamentoPixAssociatedData.Criar(pix.Id,cashbackId,usuarioId,valor,pix.TipoChavePix));
        using var inserir=Comando(c,t,"""
            INSERT INTO pagamentos_pix(id,cashback_id,usuario_beneficiario_id,valor,tipo_chave_pix,chave_pix_ciphertext,chave_pix_nonce,chave_pix_tag,encryption_version,status,quantidade_tentativas,created_at,updated_at)
            VALUES(@id,@cashback,@usuario,@valor,@tipo,@cipher,@nonce,@tag,@versao,0,0,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
            """,("id",pix.Id),("cashback",cashbackId),("usuario",usuarioId),("valor",valor),("tipo",(int)pix.TipoChavePix),("cipher",protegido.Ciphertext),("nonce",protegido.Nonce),("tag",protegido.Tag),("versao",protegido.EncryptionVersion));
        await inserir.ExecuteNonQueryAsync(ct); await Interceptar("OrdemPix",c,t);
    }
    public async Task<IReadOnlyList<Guid>> CandidatosAsync(int limite,CancellationToken ct)
    {
        if(limite is <1 or >100) throw new ArgumentOutOfRangeException(nameof(limite));
        await using var c=factory.Create(); await c.OpenAsync(ct);
        using var cmd=Comando(c,null,"""
            SELECT c.id FROM cashbacks c JOIN indicacoes i ON i.id=c.indicacao_id
            WHERE c.status=1 AND i.status=2 AND EXISTS(SELECT 1 FROM dados_pix d WHERE d.usuario_id=c.usuario_indicador_id)
              AND NOT EXISTS(SELECT 1 FROM pagamentos_pix p WHERE p.cashback_id=c.id)
            ORDER BY c.created_at,c.id LIMIT @limite
            """,("limite",limite));
        await using var r=await cmd.ExecuteReaderAsync(ct); var ids=new List<Guid>();
        while(await r.ReadAsync(ct)) ids.Add(r.ObterGuid("id")); return ids;
    }
    private static async Task<Cashback?> ObterCashback(MySqlConnection c,MySqlTransaction t,Guid pagamentoId,CancellationToken ct)
    {
        using var cmd=Comando(c,t,"SELECT * FROM cashbacks WHERE pagamento_vistoria_id=@id FOR UPDATE",("id",pagamentoId));
        await using var r=await cmd.ExecuteReaderAsync(ct); return await r.ReadAsync(ct)?CashbackMySqlRepository.Materializar(r):null;
    }
}

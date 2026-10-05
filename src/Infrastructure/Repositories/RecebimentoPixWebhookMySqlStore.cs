using Application.Recebimentos;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Database;
using MySqlConnector;
using static Infrastructure.Repositories.CobrancaPixVistoriaMySqlStore;

namespace Infrastructure.Repositories;

public sealed class RecebimentoPixWebhookMySqlStore(MySqlConnectionFactory factory) : IRecebimentoPixWebhookStore
{
    internal Func<string, MySqlConnection, MySqlTransaction, Task> Interceptar { get; init; } = (_, _, _) => Task.CompletedTask;
    public async Task PersistirAsync(IReadOnlyList<EventoPix> eventos, CancellationToken ct)
    {
        await using var c = factory.Create(); await c.OpenAsync(ct); await using var t = await c.BeginTransactionAsync(ct);
        await PersistirNaTransacao(c,t,eventos,ct); await Interceptar("Inbox",c,t); await t.CommitAsync(ct);
    }
    internal static async Task PersistirNaTransacao(MySqlConnection c, MySqlTransaction t, IReadOnlyList<EventoPix> eventos, CancellationToken ct)
    {
        if (eventos.Count is < 1 or > 100) throw new ArgumentException("Lote Pix inválido.");
        var agora = await Agora(c,t,ct);
        foreach (var original in eventos)
        {
            var e = RecebimentoPixValidacao.Canonicalizar(original);
            if (!RecebimentoPixValidacao.EventoValido(e,agora)) throw new ArgumentException("Evento Pix inválido.");
            using var cmd = Comando(c,t,"""
                INSERT INTO recebimentos_pix_inbox(id,evento_hash,end_to_end_id,txid,valor,horario,status,proxima_consulta_em,created_at,updated_at)
                VALUES(@id,@hash,@e2e,@txid,@valor,@horario,0,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE evento_hash=evento_hash
                """,("id",Guid.NewGuid()),("hash",RecebimentoPixValidacao.HashEvento(e)),("e2e",e.EndToEndId),("txid",e.Txid),("valor",e.Valor),("horario",e.Horario));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
    public async Task<PreparacaoRecebimento?> AdquirirAsync(Guid id, CancellationToken ct)
    {
        await using var c = factory.Create(); await c.OpenAsync(ct); await using var t = await c.BeginTransactionAsync(ct);
        using var cmd = Comando(c,t,"SELECT * FROM recebimentos_pix_inbox WHERE id=@id FOR UPDATE",("id",id));
        EventoPix e;
        var agora = await Agora(c,t,ct);
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct) || r.GetInt32("status") is 2 or 3 || Data(r,"lease_expira_em")>agora
                || (!r.IsDBNull(r.GetOrdinal("codigo")) && r.GetString("codigo")=="bloqueio-operacional")) return null;
            e = new(r.GetString("end_to_end_id"),r.GetString("txid"),r.GetDecimal("valor"),Data(r,"horario")!.Value);
        }
        var token = Guid.NewGuid();
        using var update = Comando(c,t,"UPDATE recebimentos_pix_inbox SET status=1,lease_id=@token,lease_expira_em=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE),updated_at=UTC_TIMESTAMP(6) WHERE id=@id",("token",token),("id",id));
        await update.ExecuteNonQueryAsync(ct); await t.CommitAsync(ct); return new(id,token,e);
    }
    public async Task FinalizarAsync(PreparacaoRecebimento p, ResultadoConsultaPix resultado, CancellationToken ct)
    {
        var confirmado = resultado.Situacao == SituacaoConsultaPix.Confirmado ? resultado.Evento : null;
        if (confirmado is not null) confirmado = RecebimentoPixValidacao.Canonicalizar(confirmado);
        await using var c = factory.Create(); await c.OpenAsync(ct); await using var t = await c.BeginTransactionAsync(ct);
        using (var identidade = Comando(c,t,"SELECT id FROM cobrancas_pix_vistoria WHERE txid=@txid",("txid",p.Evento.Txid)))
        {
            var valorId = await identidade.ExecuteScalarAsync(ct);
            if (valorId is not null and not DBNull)
                await BloquearPagamentoDaCobranca(c,t,valorId is Guid guid ? guid : Guid.Parse((string)valorId),ct);
        }
        var agora = await Agora(c,t,ct);
        using var inbox = Comando(c,t,"SELECT * FROM recebimentos_pix_inbox WHERE id=@id FOR UPDATE",("id",p.Id));
        await using (var r = await inbox.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct) || r.GetInt32("status")!=1 || r.ObterGuidOpcional("lease_id")!=p.LeaseId || Data(r,"lease_expira_em")<=agora)
                throw new InvalidOperationException("Executor de recebimento sem autorização.");
            if (r.GetString("txid")!=p.Evento.Txid || r.GetString("end_to_end_id")!=p.Evento.EndToEndId || r.GetDecimal("valor")!=p.Evento.Valor || Data(r,"horario")!=p.Evento.Horario)
                throw new InvalidOperationException("Inbox incompatível com preparação.");
        }
        var status = confirmado is null ? 0 : 3;
        var codigo = resultado.Situacao switch
        {
            SituacaoConsultaPix.AindaNaoDisponivel => "recebimento-ausente",
            SituacaoConsultaPix.BloqueioOperacional => "bloqueio-operacional",
            _ => confirmado is null ? "consulta-indeterminada" : "evidencia-divergente"
        };
        if (confirmado is not null)
        {
            using var cob = Comando(c,t,"SELECT * FROM cobrancas_pix_vistoria WHERE txid=@txid FOR UPDATE",("txid",p.Evento.Txid));
            Guid? cobrancaId = null; Guid pagamentoId = default; int cobrancaStatus = -1; decimal valor = 0; DateTime? vence = null; string? e2e = null;
            await using (var r = await cob.ExecuteReaderAsync(ct))
            {
                if (await r.ReadAsync(ct))
                {
                    cobrancaId=r.ObterGuid("id"); pagamentoId=r.ObterGuid("pagamento_vistoria_id"); cobrancaStatus=r.GetInt32("status"); valor=r.GetDecimal("valor"); vence=Data(r,"vence_em");
                    e2e=r.IsDBNull(r.GetOrdinal("end_to_end_id"))?null:r.GetString("end_to_end_id");
                    // Uma finalização concorrente de cobrança deve resolver seu próprio lease antes da confirmação.
                    if (!r.IsDBNull(r.GetOrdinal("lease_id"))) { confirmado=null; status=0; codigo="cobranca-em-processamento"; }
                }
            }
            if (cobrancaId is not null && confirmado is not null)
            {
                using var pagamento = Comando(c,t,"SELECT * FROM pagamentos_vistoria WHERE id=@id FOR UPDATE",("id",pagamentoId));
                int pagamentoStatus; decimal pagamentoValor; DateTime? pagoEm;
                PagamentoVistoria entidade;
                await using (var r = await pagamento.ExecuteReaderAsync(ct))
                {
                    if (!await r.ReadAsync(ct)) throw new InvalidOperationException("Pagamento ausente.");
                    pagamentoStatus=r.GetInt32("status"); pagamentoValor=r.GetDecimal("valor"); pagoEm=Data(r,"pago_em");
                    entidade=PagamentoVistoria.Reidratar(pagamentoId,r.ObterGuid("vistoria_id"),pagamentoValor,
                        (StatusPagamentoVistoria)pagamentoStatus,pagoEm,Data(r,"created_at")!.Value,Data(r,"updated_at")!.Value);
                }
                using var proprietario = Comando(c,t,"SELECT COUNT(*) FROM cobrancas_pix_vistoria WHERE end_to_end_id=@e2e AND id<>@id",("e2e",confirmado.EndToEndId),("id",cobrancaId));
                var pertenceAOutraCobranca = Convert.ToInt64(await proprietario.ExecuteScalarAsync(ct)) != 0;
                var coerente = !pertenceAOutraCobranca && confirmado == p.Evento
                    && RecebimentoPixValidacao.EventoValido(confirmado,agora) && confirmado.Horario<=agora
                    && confirmado.Valor==valor && valor==pagamentoValor;
                if (coerente && cobrancaStatus==5 && pagamentoStatus==1 && e2e==confirmado.EndToEndId && pagoEm==confirmado.Horario)
                { status=2; codigo="ja-confirmado"; }
                else if (coerente && cobrancaStatus is 2 or 3 or 4 && pagamentoStatus==0 && vence is not null && confirmado.Horario<=vence && e2e is null)
                {
                    // A evidência já está na inbox bloqueada e foi confrontada com
                    // GET autenticado. O Domain recebe o horário real, não UtcNow.
                    entidade.ConfirmarRecebimento(p.Id,confirmado.Horario,agora);
                    using var confirmarCob = Comando(c,t,"UPDATE cobrancas_pix_vistoria SET status=5,end_to_end_id=@e2e,confirmado_em=@horario,codigo='confirmado',updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND end_to_end_id IS NULL",("id",cobrancaId),("e2e",confirmado.EndToEndId),("horario",confirmado.Horario));
                    await confirmarCob.ExecuteNonQueryAsync(ct); await Interceptar("CobrancaAntesPagamento",c,t);
                    using var confirmarPag = Comando(c,t,"UPDATE pagamentos_vistoria SET status=1,pago_em=@horario,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status=0 AND valor=@valor",("id",pagamentoId),("horario",confirmado.Horario),("valor",valor));
                    if(await confirmarPag.ExecuteNonQueryAsync(ct)!=1) throw new InvalidOperationException("Confirmação financeira incompatível.");
                    status=2; codigo="confirmado";
                    await NotificacoesNaTransacao.PagamentoConfirmado(c,t,pagamentoId,ct);
                }
                else
                {
                    // Recebimento tardio de uma cobrança já substituída fica na
                    // inbox divergente; não reativa a identidade terminal antiga.
                    using var div = Comando(c,t,"UPDATE cobrancas_pix_vistoria SET status=CASE WHEN status IN (6,8,9) THEN status ELSE 10 END,codigo='evidencia-divergente',updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status<>5",("id",cobrancaId));
                    await div.ExecuteNonQueryAsync(ct);
                }
                using var audit = Comando(c,t,"INSERT INTO operacoes_cobranca_pix(id,cobranca_id,inbox_id,tipo,codigo,started_at,finished_at) VALUES(@id,@cob,@inbox,3,@codigo,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))",("id",Guid.NewGuid()),("cob",cobrancaId),("inbox",p.Id),("codigo",codigo));
                await audit.ExecuteNonQueryAsync(ct);
            }
        }
        using var finalizar = Comando(c,t,"UPDATE recebimentos_pix_inbox SET status=@status,codigo=@codigo,lease_id=NULL,lease_expira_em=NULL,updated_at=UTC_TIMESTAMP(6),proxima_consulta_em=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 30 SECOND) WHERE id=@id AND lease_id=@token AND lease_expira_em>UTC_TIMESTAMP(6)",("status",status),("codigo",codigo),("id",p.Id),("token",p.LeaseId));
        if(await finalizar.ExecuteNonQueryAsync(ct)!=1) throw new InvalidOperationException("Lease de recebimento expirou.");
        if(status==3 || codigo=="bloqueio-operacional") await NotificacoesNaTransacao.Criar(c,t,Application.Jornada.TipoNotificacao.CobrancaRevisao,p.Id,null,ct);
        await Interceptar("InboxFinalizado",c,t); await t.CommitAsync(ct);
    }
}

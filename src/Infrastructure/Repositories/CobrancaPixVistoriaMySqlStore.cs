using System.Data;
using Application.Recebimentos;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Infrastructure.Database;
using Infrastructure.Security;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed partial class CobrancaPixVistoriaMySqlStore(MySqlConnectionFactory factory, CobrancaPixProtector protector)
    : ICobrancaPixVistoriaStore, IRecebimentoPixCandidatoStore
{
    internal Func<string, MySqlConnection, MySqlTransaction, Task> Interceptar { get; init; } = (_, _, _) => Task.CompletedTask;

    public async Task<GeracaoCobranca> PrepararAsync(Guid pagamentoId, int expiracaoSegundos, CancellationToken ct)
    {
        await using var c = factory.Create(); await c.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        using var pagamento = Comando(c, t, "SELECT status,valor FROM pagamentos_vistoria WHERE id=@id FOR UPDATE", ("id", pagamentoId));
        decimal valor;
        await using (var r = await pagamento.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new PagamentoVistoriaNaoEncontradoException();
            if (r.GetInt32(0) != 0) throw new DomainException("Pagamento não está pendente.");
            valor = r.GetDecimal(1);
        }
        using var atual = Comando(c, t, "SELECT id,status FROM cobrancas_pix_vistoria WHERE pagamento_vistoria_id=@id ORDER BY created_at DESC,id DESC LIMIT 1 FOR UPDATE", ("id", pagamentoId));
        await using (var r = await atual.ExecuteReaderAsync(ct))
        {
            if (await r.ReadAsync(ct))
            {
                var status = (StatusCobrancaPixVistoria)r.GetInt32(1);
                if (!CobrancaPixVistoria.PermiteReemissao(status))
                {
                    var existente = r.ObterGuid("id");
                    await r.DisposeAsync(); await t.CommitAsync(ct); return new(existente, null);
                }
            }
        }
        var agora = await Agora(c, t, ct);
        var nova = new CobrancaPixVistoria(pagamentoId, valor, expiracaoSegundos, agora);
        var token = Guid.NewGuid();
        var operacaoId = Guid.NewGuid();
        using var insert = Comando(c, t, """
            INSERT INTO cobrancas_pix_vistoria(id,pagamento_vistoria_id,valor,txid,status,expiracao_segundos,lease_id,lease_expira_em,proxima_consulta_em,created_at,updated_at)
            VALUES(@id,@pagamento,@valor,@txid,1,@prazo,@token,DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
            INSERT INTO operacoes_cobranca_pix(id,cobranca_id,tipo,started_at) VALUES(@operacao,@id,0,UTC_TIMESTAMP(6));
            """, ("id", nova.Id), ("pagamento", pagamentoId), ("valor", valor), ("txid", nova.Txid), ("prazo", expiracaoSegundos), ("token",token), ("operacao",operacaoId));
        await insert.ExecuteNonQueryAsync(ct);
        await Interceptar("Preparacao", c, t);
        await t.CommitAsync(ct);
        return new(nova.Id, new(nova.Id,pagamentoId,nova.Txid,valor,expiracaoSegundos,token,operacaoId,OperacaoCobranca.Criar));
    }

    public async Task<PreparacaoCobranca?> AdquirirAsync(Guid id, CancellationToken ct)
    {
        await using var c = factory.Create(); await c.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await BloquearPagamentoDaCobranca(c, t, id, ct);
        var row = await Ler(c, t, id, ct);
        if (row is null || row.Status is 5 or 6 or 8 or 9 or 10) return null;
        if (row.LeaseExpira > await Agora(c, t, ct)) return null;
        // Após crash, nunca repete PUT diretamente: primeiro consulta o mesmo txid.
        var operacao = row.Status == 0 ? OperacaoCobranca.Criar
            : row.Remocao && row.Status == 2 ? OperacaoCobranca.Remover : OperacaoCobranca.Consultar;
        var lease = Guid.NewGuid(); var audit = Guid.NewGuid();
        using var update = Comando(c, t, """
            UPDATE cobrancas_pix_vistoria SET lease_id=@lease,lease_expira_em=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE),
              status=@status,updated_at=UTC_TIMESTAMP(6) WHERE id=@id;
            UPDATE operacoes_cobranca_pix SET codigo='executor-substituido',finished_at=UTC_TIMESTAMP(6) WHERE cobranca_id=@id AND finished_at IS NULL;
            INSERT INTO operacoes_cobranca_pix(id,cobranca_id,tipo,started_at) VALUES(@audit,@id,@tipo,UTC_TIMESTAMP(6));
            """, ("id", id), ("lease", lease), ("audit", audit), ("tipo", (int)operacao),
            ("status", operacao == OperacaoCobranca.Criar ? 1 : operacao == OperacaoCobranca.Remover ? 7 : row.Status));
        await update.ExecuteNonQueryAsync(ct); await t.CommitAsync(ct);
        return new(id, row.PagamentoId, row.Txid, row.Valor, row.Prazo, lease, audit, operacao);
    }

    public async Task FinalizarAsync(PreparacaoCobranca p, ResultadoCobrancaProvider result, CancellationToken ct)
    {
        if (result.CriadaEm is { } criada)
            result=result with { CriadaEm=new DateTime(criada.Ticks-criada.Ticks%10,criada.Kind) };
        await using var c = factory.Create(); await c.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await BloquearPagamentoDaCobranca(c, t, p.Id, ct);
        var r = await Ler(c, t, p.Id, ct) ?? throw new InvalidOperationException("Cobrança ausente.");
        var agora = await Agora(c, t, ct);
        if (r.LeaseId != p.LeaseId || r.LeaseExpira <= agora || r.Txid != p.Txid || r.Valor != p.Valor || r.PagamentoId != p.PagamentoId || r.Prazo != p.ExpiracaoSegundos)
            throw new InvalidOperationException("Executor de cobrança sem autorização.");
        var status = r.Remocao ? 7 : 3;
        string codigo = "indeterminado";
        DadosPixProtegido? pix = null;
        DateTime? vence = null;
        var valido = result.Txid == r.Txid && result.Valor == r.Valor
            && result.CriadaEm is { Kind: DateTimeKind.Utc } && result.CriadaEm <= agora
            && result.ExpiracaoSegundos == r.Prazo && result.Revisao is >= 0
            && (r.ProviderCriadoEm is null || r.ProviderCriadoEm == result.CriadaEm);
        if (result.Situacao == SituacaoCobrancaProvider.Ausente && p.Operacao == OperacaoCobranca.Consultar)
        {
            // A ausência permite nova invocação PUT apenas com esta identidade persistida.
            status = r.Remocao ? 7 : 0; codigo = "ausente";
        }
        else if (valido)
        {
            vence = result.CriadaEm!.Value.AddSeconds(result.ExpiracaoSegundos!.Value);
            status = result.Situacao switch
            {
                SituacaoCobrancaProvider.Ativa => vence <= agora ? 6 : 2,
                SituacaoCobrancaProvider.Concluida => 4,
                SituacaoCobrancaProvider.Removida => 8,
                _ => status
            };
            codigo = status switch { 2 => "ativa", 4 => "confirmacao-pendente", 6 => "expirada", 8 => "removida", _ => "indeterminado" };
            if (status == 2 && !string.IsNullOrWhiteSpace(result.PixCopiaECola)) pix = protector.Proteger(p.Id, result.PixCopiaECola);
            if (status == 4 && result.Recebimentos is { Count: > 0 })
                await RecebimentoPixWebhookMySqlStore.PersistirNaTransacao(c, t, result.Recebimentos, ct);
        }
        else if (result.Situacao is not (SituacaoCobrancaProvider.Indeterminada or SituacaoCobrancaProvider.Ausente))
        { status = 10; codigo = "dados-divergentes"; }
        using var audit = Comando(c, t, "UPDATE operacoes_cobranca_pix SET codigo=@codigo,finished_at=UTC_TIMESTAMP(6) WHERE id=@audit AND cobranca_id=@id AND tipo=@tipo AND finished_at IS NULL",
            ("codigo", codigo), ("audit", p.OperacaoId), ("id", p.Id), ("tipo", (int)p.Operacao));
        if (await audit.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Auditoria de cobrança incompatível.");
        await Interceptar("Auditoria", c, t);
        using var update = Comando(c, t, """
            UPDATE cobrancas_pix_vistoria SET status=@status,codigo=@codigo,
              provider_criado_em=COALESCE(provider_criado_em,@criada),vence_em=COALESCE(vence_em,@vence),revisao=COALESCE(@revisao,revisao),
              pix_ciphertext=COALESCE(@cipher,pix_ciphertext),pix_nonce=COALESCE(@nonce,pix_nonce),pix_tag=COALESCE(@tag,pix_tag),encryption_version=COALESCE(@version,encryption_version),
              lease_id=NULL,lease_expira_em=NULL,proxima_consulta_em=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 30 SECOND),updated_at=UTC_TIMESTAMP(6)
            WHERE id=@id AND lease_id=@lease AND lease_expira_em>UTC_TIMESTAMP(6)
            """, ("id", p.Id), ("lease", p.LeaseId), ("status", status), ("codigo", codigo), ("criada", valido ? result.CriadaEm : null),
            ("vence", vence), ("revisao", valido ? result.Revisao : null), ("cipher", pix?.Ciphertext), ("nonce", pix?.Nonce), ("tag", pix?.Tag), ("version", pix?.EncryptionVersion));
        if (await update.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Lease de cobrança expirou.");
        if (status == 8 && r.Remocao)
        {
            using var cancel = Comando(c, t, "UPDATE pagamentos_vistoria SET status=2,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status=0", ("id", r.PagamentoId));
            if (await cancel.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Pagamento incompatível com remoção.");
        }
        await Interceptar("Finalizacao", c, t); await t.CommitAsync(ct);
    }

    internal static MySqlCommand Comando(MySqlConnection c, MySqlTransaction? t, string sql, params (string Nome, object? Valor)[] args)
    {
        var cmd = new MySqlCommand(sql, c, t);
        foreach (var (nome, valor) in args) cmd.Parameters.AddWithValue("@" + nome, valor is Guid guid ? guid.ToString() : valor ?? DBNull.Value);
        return cmd;
    }
    internal static async Task BloquearPagamentoDaCobranca(MySqlConnection c, MySqlTransaction t, Guid id, CancellationToken ct)
    {
        // A identidade é imutável. Todos os comandos financeiros adquirem primeiro
        // a linha do pagamento, antes da cobrança ou da inbox relacionada.
        using var buscar = Comando(c, t, "SELECT pagamento_vistoria_id FROM cobrancas_pix_vistoria WHERE id=@id", ("id", id));
        var pagamentoId = await buscar.ExecuteScalarAsync(ct);
        if (pagamentoId is null or DBNull) return;
        using var bloquear = Comando(c, t, "SELECT id FROM pagamentos_vistoria WHERE id=@id FOR UPDATE", ("id", pagamentoId));
        await bloquear.ExecuteScalarAsync(ct);
    }
    internal static async Task<DateTime> Agora(MySqlConnection c, MySqlTransaction t, CancellationToken ct)
    { using var cmd = Comando(c, t, "SELECT UTC_TIMESTAMP(6)"); return DateTime.SpecifyKind((DateTime)(await cmd.ExecuteScalarAsync(ct))!, DateTimeKind.Utc); }
    internal static DateTime? Data(MySqlDataReader r, string nome) => r.IsDBNull(r.GetOrdinal(nome)) ? null : DateTime.SpecifyKind(r.GetDateTime(nome), DateTimeKind.Utc);
    private sealed record Linha(Guid Id, Guid PagamentoId, string Txid, decimal Valor, int Prazo, int Status, Guid? LeaseId, DateTime? LeaseExpira, bool Remocao, DateTime? ProviderCriadoEm);
    private static async Task<Linha?> Ler(MySqlConnection c, MySqlTransaction t, Guid id, CancellationToken ct)
    {
        using var cmd = Comando(c, t, "SELECT * FROM cobrancas_pix_vistoria WHERE id=@id FOR UPDATE", ("id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(id, r.ObterGuid("pagamento_vistoria_id"), r.GetString("txid"), r.GetDecimal("valor"), r.GetInt32("expiracao_segundos"), r.GetInt32("status"), r.ObterGuidOpcional("lease_id"), Data(r,"lease_expira_em"), r.GetBoolean("remocao_solicitada"), Data(r,"provider_criado_em")) : null;
    }
}

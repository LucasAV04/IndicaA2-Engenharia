using Application.Recebimentos;
using Domain.Enums;
using Domain.Exceptions;
using Infrastructure.Database;
using Infrastructure.Security;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed partial class CobrancaPixVistoriaMySqlStore
{
    public async Task<IReadOnlyList<CobrancaAdministrativa>> ListarAsync(CancellationToken ct)
    {
        await using var c = factory.Create(); await c.OpenAsync(ct);
        using var cmd = Comando(c, null, """
            SELECT c.id,c.pagamento_vistoria_id,c.valor,c.status,c.vence_em,c.confirmado_em,
              p.vistoria_id,u.nome AS cliente,(c.status=10 OR c.codigo='evidencia-divergente') AS divergencia
            FROM cobrancas_pix_vistoria c
            JOIN pagamentos_vistoria p ON p.id=c.pagamento_vistoria_id
            JOIN vistorias v ON v.id=p.vistoria_id JOIN usuarios u ON u.id=v.usuario_id
            ORDER BY c.created_at DESC,c.id
            """);
        await using var r = await cmd.ExecuteReaderAsync(ct); var rows = new List<CobrancaAdministrativa>();
        while (await r.ReadAsync(ct)) rows.Add(new(r.ObterGuid("id"), r.ObterGuid("pagamento_vistoria_id"), r.GetDecimal("valor"), (StatusCobrancaPixVistoria)r.GetInt32("status"), Data(r,"vence_em"), Data(r,"confirmado_em"),
            r.ObterGuid("vistoria_id"),r.GetString("cliente"),!r.IsDBNull(r.GetOrdinal("divergencia")) && r.GetBoolean("divergencia")));
        return rows;
    }
    public async Task<IReadOnlyList<AuditoriaCobranca>> AuditoriaAsync(Guid id, CancellationToken ct)
    {
        await using var c = factory.Create(); await c.OpenAsync(ct);
        using var cmd = Comando(c, null, "SELECT id,tipo,codigo,started_at,finished_at FROM operacoes_cobranca_pix WHERE cobranca_id=@id ORDER BY started_at,id", ("id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct); var rows = new List<AuditoriaCobranca>();
        while (await r.ReadAsync(ct)) rows.Add(new(r.ObterGuid("id"), r.GetInt32("tipo"), r.IsDBNull(2) ? null : r.GetString(2), Data(r,"started_at")!.Value, Data(r,"finished_at")));
        return rows;
    }
    public async Task<IndicadoresRecebimento> IndicadoresAsync(CancellationToken ct)
    {
        await using var c = factory.Create(); await c.OpenAsync(ct);
        using var cmd = Comando(c, null, "SELECT COALESCE(SUM(status=2),0),COALESCE(SUM(status=4),0),COALESCE(SUM(status=5),0),COALESCE(SUM(status=6),0),COALESCE(SUM(status=10),0),(SELECT COUNT(*) FROM recebimentos_pix_inbox WHERE status=3) FROM cobrancas_pix_vistoria");
        await using var r = await cmd.ExecuteReaderAsync(ct); await r.ReadAsync(ct);
        return new(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.GetInt64(3),r.GetInt64(4),r.GetInt64(5));
    }
    public async Task RotacionarLinkAsync(Guid id, byte[] hash, DateTime expiraEm, CancellationToken ct)
    {
        if (hash.Length != 32) throw new ArgumentException("Hash inválido.");
        await using var c = factory.Create(); await c.OpenAsync(ct);
        using var cmd = Comando(c, null, "UPDATE cobrancas_pix_vistoria SET link_hash=@hash,link_expira_em=@expira,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status NOT IN (6,8,9,10)", ("hash", hash), ("expira", expiraEm), ("id", id));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new DomainException("Cobrança indisponível para link.");
    }
    public async Task<CobrancaPublica?> ObterPublicaAsync(byte[] hash, CancellationToken ct)
    {
        if (hash.Length != 32) return null;
        await using var c = factory.Create(); await c.OpenAsync(ct);
        using var cmd = Comando(c, null, "SELECT id,valor,status,vence_em,pix_ciphertext,pix_nonce,pix_tag,encryption_version,link_hash,UTC_TIMESTAMP(6) AS agora FROM cobrancas_pix_vistoria WHERE link_hash=@hash AND link_expira_em>UTC_TIMESTAMP(6)", ("hash", hash));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash, (byte[])r["link_hash"])) return null;
        var status = (StatusCobrancaPixVistoria)r.GetInt32("status");
        var vence = Data(r,"vence_em"); string? pix = null;
        // Relógio do banco define a validade, inclusive antes da próxima reconciliação.
        var agora = Data(r,"agora")!.Value;
        if (status == StatusCobrancaPixVistoria.Ativa && vence > agora && !r.IsDBNull(r.GetOrdinal("pix_ciphertext")))
            pix = protector.Desproteger(r.ObterGuid("id"), new DadosPixProtegido((byte[])r["pix_ciphertext"], (byte[])r["pix_nonce"], (byte[])r["pix_tag"], r.GetInt32("encryption_version")));
        if (status == StatusCobrancaPixVistoria.Ativa && vence <= agora) status = StatusCobrancaPixVistoria.Expirada;
        return new(r.GetDecimal("valor"), status, vence, pix, status == StatusCobrancaPixVistoria.Confirmada);
    }
    public async Task SolicitarCancelamentoAsync(Guid pagamentoId, CancellationToken ct)
    {
        await using var c = factory.Create(); await c.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        using var p = Comando(c,t,"SELECT status FROM pagamentos_vistoria WHERE id=@id FOR UPDATE",("id",pagamentoId));
        var status = await p.ExecuteScalarAsync(ct);
        if (status is null) throw new PagamentoVistoriaNaoEncontradoException();
        if (Convert.ToInt32(status) == 1) throw new DomainException("Pagamento confirmado não pode ser cancelado.");
        if (Convert.ToInt32(status) == 2) { await t.CommitAsync(ct); return; }
        using var atual = Comando(c,t,"SELECT COUNT(*) FROM cobrancas_pix_vistoria WHERE pagamento_vistoria_id=@id",("id",pagamentoId));
        if (Convert.ToInt64(await atual.ExecuteScalarAsync(ct)) == 0)
        {
            using var cancel = Comando(c,t,"UPDATE pagamentos_vistoria SET status=2,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status=0",("id",pagamentoId));
            await cancel.ExecuteNonQueryAsync(ct);
        }
        else
        {
            using var pendente = Comando(c,t,"SELECT COUNT(*) FROM cobrancas_pix_vistoria WHERE pagamento_vistoria_id=@id AND status NOT IN (6,8)",("id",pagamentoId));
            if (Convert.ToInt64(await pendente.ExecuteScalarAsync(ct))==0)
            {
                using var cancelarTerminal = Comando(c,t,"UPDATE pagamentos_vistoria SET status=2,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND status=0",("id",pagamentoId));
                await cancelarTerminal.ExecuteNonQueryAsync(ct);
                await t.CommitAsync(ct); return;
            }
            using var solicitar = Comando(c,t,"UPDATE cobrancas_pix_vistoria SET remocao_solicitada=TRUE,proxima_consulta_em=UTC_TIMESTAMP(6),updated_at=UTC_TIMESTAMP(6) WHERE pagamento_vistoria_id=@id AND status NOT IN (5,6,8,9,10)",("id",pagamentoId));
            if (await solicitar.ExecuteNonQueryAsync(ct) == 0) throw new DomainException("Cancelamento requer cobrança compatível e confirmação de remoção.");
        }
        await t.CommitAsync(ct);
    }
    public Task<IReadOnlyList<Guid>> CobrançasAsync(int limite, CancellationToken ct) => Candidatos(false, limite, ct);
    public Task<IReadOnlyList<Guid>> EventosAsync(int limite, CancellationToken ct) => Candidatos(true, limite, ct);
    private async Task<IReadOnlyList<Guid>> Candidatos(bool inbox, int limite, CancellationToken ct)
    {
        if (limite is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limite));
        await using var c = factory.Create(); await c.OpenAsync(ct);
        var sql = inbox
            ? "SELECT id FROM recebimentos_pix_inbox WHERE status IN (0,1) AND COALESCE(codigo,'')<>'bloqueio-operacional' AND proxima_consulta_em<=UTC_TIMESTAMP(6) AND (lease_id IS NULL OR lease_expira_em<=UTC_TIMESTAMP(6)) ORDER BY proxima_consulta_em,id LIMIT @limite"
            : "SELECT id FROM cobrancas_pix_vistoria WHERE status IN (0,1,2,3,4,7) AND COALESCE(codigo,'')<>'bloqueio-operacional' AND proxima_consulta_em<=UTC_TIMESTAMP(6) AND (lease_id IS NULL OR lease_expira_em<=UTC_TIMESTAMP(6)) ORDER BY proxima_consulta_em,id LIMIT @limite";
        using var cmd = Comando(c,null,sql,("limite",limite)); await using var r = await cmd.ExecuteReaderAsync(ct);
        var ids = new List<Guid>(); while (await r.ReadAsync(ct)) ids.Add(r.ObterGuid("id")); return ids;
    }
}

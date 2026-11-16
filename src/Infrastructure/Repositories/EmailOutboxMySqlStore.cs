using System.Data;
using Infrastructure.Database;
using Infrastructure.Security;
using Application.Contas;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed record EnvioEmail(Guid Id, Guid Executor, Guid Usuario, Guid Referencia, string Email,
    TemplateEmail Template, int Tentativa, TokenContaCifrado? Material);

public interface IEmailOutboxStore
{
    Task<IReadOnlyList<Guid>> SelecionarAsync(int limite, CancellationToken ct);
    Task<EnvioEmail?> AdquirirAsync(Guid id, CancellationToken ct);
    Task FinalizarAsync(EnvioEmail envio, EstadoEmail estado, int? erro, CancellationToken ct);
}

public sealed class EmailOutboxMySqlStore(MySqlConnectionFactory factory) : IEmailOutboxStore
{
    public async Task<IReadOnlyList<Guid>> SelecionarAsync(int limite, CancellationToken ct)
    {
        await using var c = factory.Create();
        await c.OpenAsync(ct);

        // Copia somente notificações criadas depois da migration. Os registros financeiros e os pagamentos
        // terminam em suas transações originais, independentemente da entrega SMTP.
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var eventos = Cmd(c, """
            INSERT INTO email_outbox(id,usuario_id,template,referencia_id,proxima_tentativa_em,criado_em)
            SELECT UUID(),n.usuario_id,n.tipo+2,n.referencia_id,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6)
            FROM notificacoes_internas n
            LEFT JOIN email_outbox o ON o.template=n.tipo+2 AND o.referencia_id=n.referencia_id
                AND o.usuario_id=n.usuario_id AND o.canal=0
            WHERE n.elegivel_email=TRUE AND n.escopo=0 AND n.tipo BETWEEN 0 AND 4 AND o.id IS NULL
            ORDER BY n.created_at,n.id LIMIT @limite
            ON DUPLICATE KEY UPDATE email_outbox.id=email_outbox.id
            """, ("@limite", limite));
        eventos.Transaction = tx;
        await eventos.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);

        await using var expired = Cmd(c, """
            UPDATE email_outbox o JOIN usuarios u ON u.id=o.usuario_id
                LEFT JOIN tokens_conta t ON t.id=o.token_id
            SET o.status=IF(o.tentativa=5 AND o.status=1,4,5),o.lease_expira_em=NULL,o.executor_id=NULL,
                o.token_cifrado=NULL,o.nonce=NULL,o.tag=NULL,o.versao_cifra=NULL,o.falhou_em=UTC_TIMESTAMP(6)
            WHERE (o.status IN (0,4) OR (o.status=1 AND o.lease_expira_em<=UTC_TIMESTAMP(6)))
              AND (u.status<>1 OR (o.status=1 AND o.tentativa=5)
                OR (t.id IS NOT NULL AND (t.expira_em<=UTC_TIMESTAMP(6) OR t.revogado_em IS NOT NULL
                    OR t.consumido_em IS NOT NULL OR t.email_hash<>UNHEX(SHA2(u.email,256)))))
              AND (o.status<>4 OR o.tentativa<5)
            """);
        await expired.ExecuteNonQueryAsync(ct);

        await using var select = Cmd(c, """
            SELECT id FROM email_outbox WHERE tentativa<5
              AND ((status IN (0,4) AND proxima_tentativa_em<=UTC_TIMESTAMP(6))
                OR (status=1 AND lease_expira_em<=UTC_TIMESTAMP(6)))
            ORDER BY proxima_tentativa_em,id LIMIT @limite
            """, ("@limite", limite));
        await using var r = await select.ExecuteReaderAsync(ct);
        var result = new List<Guid>();
        while (await r.ReadAsync(ct))
            result.Add(r.ObterGuid("id"));
        return result;
    }

    public async Task<EnvioEmail?> AdquirirAsync(Guid id, CancellationToken ct)
    {
        await using var c = factory.Create();
        await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var executor = Guid.NewGuid();

        await using var claim = Cmd(c, """
            UPDATE email_outbox o JOIN usuarios u ON u.id=o.usuario_id
                LEFT JOIN tokens_conta t ON t.id=o.token_id
            SET o.status=1,o.tentativa=o.tentativa+1,o.executor_id=@executor,
                o.lease_expira_em=UTC_TIMESTAMP(6)+INTERVAL 5 MINUTE
            WHERE o.id=@id AND o.tentativa<5 AND u.status=1
              AND ((o.status IN (0,4) AND o.proxima_tentativa_em<=UTC_TIMESTAMP(6))
                OR (o.status=1 AND o.lease_expira_em<=UTC_TIMESTAMP(6)))
              AND (o.token_id IS NULL OR (t.expira_em>UTC_TIMESTAMP(6) AND t.revogado_em IS NULL
                AND t.consumido_em IS NULL AND t.email_hash=UNHEX(SHA2(u.email,256))
                AND o.token_cifrado IS NOT NULL))
            """, ("@id", id.ToString()), ("@executor", executor.ToString()));
        claim.Transaction = tx;
        if (await claim.ExecuteNonQueryAsync(ct) != 1)
            return null;

        await using var select = Cmd(c, """
            SELECT o.usuario_id,o.referencia_id,u.email,o.template,o.tentativa,
                o.token_cifrado,o.nonce,o.tag,o.versao_cifra
            FROM email_outbox o JOIN usuarios u ON u.id=o.usuario_id WHERE o.id=@id
            """, ("@id", id.ToString()));
        select.Transaction = tx;
        EnvioEmail envio;
        await using (var r = await select.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct))
                return null;
            envio = new EnvioEmail(id, executor, r.ObterGuid("usuario_id"), r.ObterGuid("referencia_id"),
                r.GetString(2), (TemplateEmail)r.GetByte(3), r.GetInt32(4),
                r.IsDBNull(5) ? null : new TokenContaCifrado(r.GetFieldValue<byte[]>(5),
                    r.GetFieldValue<byte[]>(6), r.GetFieldValue<byte[]>(7), r.GetByte(8)));
        }
        await tx.CommitAsync(ct);
        return envio;
    }

    public async Task FinalizarAsync(EnvioEmail envio, EstadoEmail estado, int? erro, CancellationToken ct)
    {
        var manterMaterial = (estado is EstadoEmail.Pendente or EstadoEmail.Indeterminado) && envio.Tentativa < 5;
        await using var c = factory.Create();
        await c.OpenAsync(ct);
        await using var finish = Cmd(c, """
            UPDATE email_outbox SET status=@estado,executor_id=NULL,lease_expira_em=NULL,
                erro=@erro,proxima_tentativa_em=TIMESTAMPADD(SECOND,@espera,UTC_TIMESTAMP(6)),
                enviado_em=IF(@estado=2,UTC_TIMESTAMP(6),NULL),
                falhou_em=IF(@estado=2,NULL,UTC_TIMESTAMP(6)),
                token_cifrado=IF(@manter,token_cifrado,NULL),nonce=IF(@manter,nonce,NULL),
                tag=IF(@manter,tag,NULL),versao_cifra=IF(@manter,versao_cifra,NULL)
            WHERE id=@id AND status=1 AND executor_id=@executor AND lease_expira_em>UTC_TIMESTAMP(6)
            """, ("@estado", (byte)estado), ("@erro", (object?)erro ?? DBNull.Value),
            ("@espera", 30 * (1 << (envio.Tentativa - 1))), ("@manter", manterMaterial),
            ("@id", envio.Id.ToString()), ("@executor", envio.Executor.ToString()));
        await finish.ExecuteNonQueryAsync(ct);
    }

    private static MySqlCommand Cmd(MySqlConnection c, string sql, params (string Name, object Value)[] parameters)
    {
        var cmd = new MySqlCommand(sql, c);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
}

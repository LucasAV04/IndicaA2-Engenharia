using Application.Contas;
using Application.Interfaces.Security;
using Domain.Enums;
using Infrastructure.Database;
using Infrastructure.Security;
using MySqlConnector;

namespace Infrastructure.Repositories;

public sealed class ContaMySqlStore(MySqlConnectionFactory factory, IPasswordHasher passwords)
{
    public async Task SolicitarAsync(string? email, Guid? usuarioId, FinalidadeTokenConta finalidade,
        TokenContaProtector protector, CancellationToken ct)
    {
        var porEmail = finalidade == FinalidadeTokenConta.RedefinicaoSenha;
        if (porEmail ? string.IsNullOrWhiteSpace(email) || email.Length > 254 : usuarioId is null)
            return;

        await using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var condition = porEmail ? "email=@identidade" : "id=@identidade AND email_confirmado=FALSE";
        await using var select = Command(tx, $"""
            SELECT id FROM usuarios WHERE {condition} AND status=@ativo FOR UPDATE
            """, ("@identidade", porEmail ? email!.Trim().ToLowerInvariant() : usuarioId!.Value.ToString()),
            ("@ativo", (int)StatusUsuario.Ativo));
        if (!Guid.TryParse((await select.ExecuteScalarAsync(ct))?.ToString(), out var id))
            return;

        var rawId = id.ToString();
        var args = new (string, object)[] { ("@usuario", rawId), ("@finalidade", (byte)finalidade) };
        await using var cooldown = Command(tx, """
            SELECT EXISTS(SELECT 1 FROM tokens_conta WHERE usuario_id=@usuario AND finalidade=@finalidade
                AND criado_em >= UTC_TIMESTAMP(6) - INTERVAL 60 SECOND)
            """, args);
        if (Convert.ToBoolean(await cooldown.ExecuteScalarAsync(ct)))
            return;

        await RevogarAsync(tx, rawId, finalidade, ct);

        var token = SegurancaConta.GerarToken();
        var reference = Guid.NewGuid();
        var material = protector.Cifrar(token, id, reference, finalidade);
        await using var insertToken = Command(tx, """
            INSERT INTO tokens_conta(id,usuario_id,finalidade,token_hash,email_hash,criado_em,expira_em)
            SELECT @id,id,@finalidade,@hash,UNHEX(SHA2(email,256)),UTC_TIMESTAMP(6),
                TIMESTAMPADD(SECOND,@segundos,UTC_TIMESTAMP(6)) FROM usuarios WHERE id=@usuario
            """, ("@id", reference.ToString()), ("@usuario", rawId), ("@finalidade", (byte)finalidade),
            ("@hash", SegurancaConta.HashToken(token)!), ("@segundos", (int)SegurancaConta.Validade(finalidade).TotalSeconds));
        await insertToken.ExecuteNonQueryAsync(ct);
        await using var enqueue = Command(tx, """
            INSERT INTO email_outbox(id,usuario_id,template,referencia_id,token_id,
                token_cifrado,nonce,tag,versao_cifra,proxima_tentativa_em,criado_em)
            VALUES (@id,@usuario,@template,@referencia,@referencia,@cifrado,@nonce,@tag,@versao,
                UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
            """, ("@id", Guid.NewGuid().ToString()), ("@usuario", rawId), ("@template", (byte)finalidade),
            ("@referencia", reference.ToString()), ("@cifrado", material.Ciphertext),
            ("@nonce", material.Nonce), ("@tag", material.Tag), ("@versao", material.Versao));
        await enqueue.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> ConsumirAsync(string? token, FinalidadeTokenConta finalidade, string? novaSenha,
        CancellationToken ct)
    {
        var hash = SegurancaConta.HashToken(token);
        var reset = finalidade == FinalidadeTokenConta.RedefinicaoSenha;
        if (hash is null || (reset && !SegurancaConta.NovaSenhaValida(novaSenha)))
            return false;

        await using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var lookup = Command(tx, """
            SELECT usuario_id FROM tokens_conta WHERE token_hash=@hash AND finalidade=@finalidade
              AND consumido_em IS NULL AND revogado_em IS NULL AND expira_em > UTC_TIMESTAMP(6)
            """, ("@hash", hash), ("@finalidade", (byte)finalidade));
        if (!Guid.TryParse((await lookup.ExecuteScalarAsync(ct))?.ToString(), out var id))
            return false;
        var rawId = id.ToString();
        await using var user = Command(tx, "SELECT status FROM usuarios WHERE id=@id FOR UPDATE", ("@id", rawId));
        if (Convert.ToInt32(await user.ExecuteScalarAsync(ct)) != (int)StatusUsuario.Ativo)
            return false;

        await using var consume = Command(tx, """
            UPDATE tokens_conta t JOIN usuarios u ON u.id=t.usuario_id
            SET t.consumido_em=UTC_TIMESTAMP(6)
            WHERE t.token_hash=@hash AND t.finalidade=@finalidade AND t.usuario_id=@usuario
              AND t.email_hash=UNHEX(SHA2(u.email,256))
              AND consumido_em IS NULL AND revogado_em IS NULL AND expira_em > UTC_TIMESTAMP(6)
            """, ("@hash", hash), ("@finalidade", (byte)finalidade), ("@usuario", rawId));
        if (await consume.ExecuteNonQueryAsync(ct) != 1)
            return false;

        const string confirm = "UPDATE usuarios SET email_confirmado=TRUE, updated_at=UTC_TIMESTAMP(6) WHERE id=@id";
        const string change = """
            UPDATE usuarios SET senha_hash=@senha, versao_credenciais=versao_credenciais+1,
                email_confirmado=TRUE,updated_at=UTC_TIMESTAMP(6) WHERE id=@id
            """;
        await using var update = Command(tx, reset ? change : confirm, ("@id", rawId));
        if (reset)
            update.Parameters.AddWithValue("@senha", passwords.HashPassword(novaSenha!));
        await update.ExecuteNonQueryAsync(ct);
        await RevogarAsync(tx, rawId, finalidade, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> AlterarSenhaAsync(Guid id, long versao, string atual, string nova, CancellationToken ct)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var rawId = id.ToString();
        await using var user = Command(tx, """
            SELECT senha_hash FROM usuarios WHERE id=@id AND status=@ativo AND versao_credenciais=@versao FOR UPDATE
            """, ("@id", rawId), ("@ativo", (int)StatusUsuario.Ativo), ("@versao", versao));
        if (await user.ExecuteScalarAsync(ct) is not string saved || !passwords.VerifyPassword(atual, saved))
            return false;
        await using var update = Command(tx, """
            UPDATE usuarios SET senha_hash=@senha,versao_credenciais=versao_credenciais+1,updated_at=UTC_TIMESTAMP(6)
            WHERE id=@id
            """, ("@senha", passwords.HashPassword(nova)), ("@id", rawId));
        await update.ExecuteNonQueryAsync(ct);
        await RevogarAsync(tx, rawId, FinalidadeTokenConta.RedefinicaoSenha, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private static async Task RevogarAsync(MySqlTransaction tx, string usuario, FinalidadeTokenConta finalidade,
        CancellationToken ct)
    {
        var parameters = new (string, object)[] { ("@usuario", usuario), ("@finalidade", (byte)finalidade) };
        await using var revoke = Command(tx, """
            UPDATE tokens_conta SET revogado_em=UTC_TIMESTAMP(6)
            WHERE usuario_id=@usuario AND finalidade=@finalidade AND consumido_em IS NULL AND revogado_em IS NULL
            """, parameters);
        await revoke.ExecuteNonQueryAsync(ct);
        await using var expire = Command(tx, """
            UPDATE email_outbox o JOIN tokens_conta t ON t.id=o.token_id
            SET o.status=5,o.executor_id=NULL,o.lease_expira_em=NULL,
                o.token_cifrado=NULL,o.nonce=NULL,o.tag=NULL,o.versao_cifra=NULL
            WHERE t.usuario_id=@usuario AND t.finalidade=@finalidade AND o.status IN (0,1,4)
                AND (t.consumido_em IS NOT NULL OR t.revogado_em IS NOT NULL)
            """, parameters);
        await expire.ExecuteNonQueryAsync(ct);
    }

    private static MySqlCommand Command(MySqlTransaction tx, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, tx.Connection, tx);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }
}

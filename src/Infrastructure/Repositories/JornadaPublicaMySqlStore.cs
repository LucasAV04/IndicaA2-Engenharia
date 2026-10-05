using System.Security.Cryptography;
using Application.Jornada;
using Infrastructure.Database;
using MySqlConnector;
using static Infrastructure.Repositories.CobrancaPixVistoriaMySqlStore;

namespace Infrastructure.Repositories;

public sealed class JornadaPublicaMySqlStore(MySqlConnectionFactory factory) : IJornadaPublicaStore
{
    public async Task<bool> CodigoUtilizavelAsync(string codigo,CancellationToken ct)
    {
        codigo=JornadaValidacao.Codigo(codigo);
        await using var c=factory.Create(); await c.OpenAsync(ct);
        using var cmd=Comando(c,null,"SELECT EXISTS(SELECT 1 FROM usuarios WHERE codigo_indicacao=@codigo AND tipo_usuario=1 AND status=1)",("codigo",codigo));
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct));
    }
    public async Task<ProtocoloIndicacao> CaptarAsync(IndicacaoPublicaRequest request,string chave,DateTime agora,CancellationToken ct)
    {
        request=JornadaValidacao.Normalizar(request);
        var hash=JornadaValidacao.HashChave(chave);
        await using var c=factory.Create(); await c.OpenAsync(ct); await using var t=await c.BeginTransactionAsync(ct);
        // Repetição de uma chave já aceita continua idempotente após desativar o link.
        // O segredo aleatório permite apenas recuperar o protocolo, nunca o conteúdo.
        using(var anterior=Comando(c,t,"SELECT protocolo_publico FROM indicacoes WHERE idempotencia_hash=@hash",("hash",hash)))
        {
            if(await anterior.ExecuteScalarAsync(ct) is string registrado)
            { await t.CommitAsync(ct); return new(registrado); }
        }
        // Constraint da chave decide a concorrência inclusive entre códigos distintos.
        // Não se compara nem devolve o conteúdo de uma submissão anterior.
        using var usuario=Comando(c,t,"SELECT id FROM usuarios WHERE codigo_indicacao=@codigo AND tipo_usuario=1 AND status=1 FOR SHARE",("codigo",request.Codigo));
        var id=await usuario.ExecuteScalarAsync(ct);
        if(id is null or DBNull) throw new ArgumentException("Link indisponível.");
        var protocolo=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        using var insert=Comando(c,t,"""
            INSERT INTO indicacoes(id,usuario_indicador_id,nome_indicada,telefone_indicada,codigo_indicacao_usado,status,created_at,updated_at,
                origem,consentimento_versao,consentimento_em,idempotencia_hash,protocolo_publico)
            VALUES(@id,@usuario,@nome,@telefone,@codigo,0,@agora,@agora,1,@versao,@agora,@hash,@protocolo)
            ON DUPLICATE KEY UPDATE idempotencia_hash=idempotencia_hash
            """,("id",Guid.NewGuid()),("usuario",id), ("nome",request.Nome),("telefone",request.Telefone),("codigo",request.Codigo),
            ("agora",agora),("versao",request.VersaoTermo),("hash",hash),("protocolo",protocolo));
        await insert.ExecuteNonQueryAsync(ct);
        using var select=Comando(c,t,"SELECT protocolo_publico FROM indicacoes WHERE idempotencia_hash=@hash FOR UPDATE",("hash",hash));
        var persisted=(string?)await select.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Protocolo não persistido.");
        await t.CommitAsync(ct); return new(persisted);
    }
}

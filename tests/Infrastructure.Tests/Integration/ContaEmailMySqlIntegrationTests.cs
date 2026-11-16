using System.Security.Cryptography;
using Application.Contas;
using Application.Relatorios;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Repositories;
using Infrastructure.Security;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", MySqlIntegrationCategory.Name)]
public sealed class ContaEmailMySqlIntegrationTests(MySqlIntegrationFixture fixture)
{
    private readonly BCryptPasswordHasher passwords = new();
    private readonly TokenContaProtector protector =
        new(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    [MySqlIntegrationFact]
    public async Task SolicitacoesConsumoEEnvioConcorrentesPreservamUsoUnico()
    {
        await fixture.LimparDadosAsync();
        var (user, repo) = await NovoUsuarioAsync();
        var accounts = new ContaMySqlStore(fixture.ConnectionFactory, passwords);
        var outbox = new EmailOutboxMySqlStore(fixture.ConnectionFactory);
        const FinalidadeTokenConta kind = FinalidadeTokenConta.RedefinicaoSenha;

        await Task.WhenAll(accounts.SolicitarAsync(user.Email, null, kind, protector, default),
            accounts.SolicitarAsync(user.Email, null, kind, protector, default));
        var id = Assert.Single(await outbox.SelecionarAsync(20, default));
        var acquired = await Task.WhenAll(outbox.AdquirirAsync(id, default), outbox.AdquirirAsync(id, default));
        var envio = Assert.Single(acquired.OfType<EnvioEmail>());
        var material = Assert.IsType<TokenContaCifrado>(envio.Material);
        var token = protector.Decifrar(material, user.Id, envio.Referencia, kind);

        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var check = new MySqlCommand("""
            SELECT COUNT(*) FROM tokens_conta WHERE token_hash=@hash
              AND expira_em=criado_em+INTERVAL 30 MINUTE
            """, connection);
        check.Parameters.AddWithValue("@hash", SegurancaConta.HashToken(token));
        Assert.Equal(1L, Convert.ToInt64(await check.ExecuteScalarAsync()));
        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes(token), material.Ciphertext);
        Assert.False(await accounts.ConsumirAsync(token, FinalidadeTokenConta.ConfirmacaoEmail, null, default));

        await outbox.FinalizarAsync(envio, EstadoEmail.Enviado, null, default);
        var results = await Task.WhenAll(accounts.ConsumirAsync(token, kind, "ficticia-nova-123", default),
            accounts.ConsumirAsync(token, kind, "ficticia-outra-123", default));
        Assert.Single(results, result => result);
        var updated = Assert.IsType<Usuario>(await repo.ObterPorIdAsync(user.Id));
        Assert.Equal(2L, updated.VersaoCredenciais);
        Assert.True(updated.EmailConfirmado);
        Assert.False(passwords.VerifyPassword("ficticia-inicial-123", updated.SenhaHash));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await new RelatorioOperacionalMySqlStore(fixture.ConnectionFactory).ConsultarAsync(
            new FiltroRelatorio(TipoRelatorio.Emails, today.AddDays(-1), today.AddDays(1), 2, 1, 1), false, default);
        Assert.Equal(id, Assert.Single(report.Linhas).Id);
        Assert.Null(report.ValorTotal);
    }

    [MySqlIntegrationFact]
    public async Task LeaseRecuperadoImpedeExecutorAntigoDeFinalizar()
    {
        await fixture.LimparDadosAsync();
        var (user, _) = await NovoUsuarioAsync();
        var accounts = new ContaMySqlStore(fixture.ConnectionFactory, passwords);
        var outbox = new EmailOutboxMySqlStore(fixture.ConnectionFactory);
        await accounts.SolicitarAsync(null, user.Id, FinalidadeTokenConta.ConfirmacaoEmail, protector, default);
        var id = Assert.Single(await outbox.SelecionarAsync(20, default));
        var primeiro = Assert.IsType<EnvioEmail>(await outbox.AdquirirAsync(id, default));
        Assert.Empty(await outbox.SelecionarAsync(20, default));

        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var expire = new MySqlCommand("""
            UPDATE email_outbox SET lease_expira_em=UTC_TIMESTAMP(6)-INTERVAL 1 SECOND WHERE id=@id
            """, connection);
        expire.Parameters.AddWithValue("@id", id.ToString());
        await expire.ExecuteNonQueryAsync();

        var segundo = Assert.IsType<EnvioEmail>(await outbox.AdquirirAsync(id, default));
        Assert.NotEqual(primeiro.Executor, segundo.Executor);
        Assert.Equal(2, segundo.Tentativa);
        await outbox.FinalizarAsync(primeiro, EstadoEmail.Enviado, null, default);
        await outbox.FinalizarAsync(segundo, EstadoEmail.FalhaPermanente, 1, default);
        await using var check = new MySqlCommand("SELECT status FROM email_outbox WHERE id=@id AND token_cifrado IS NULL", connection);
        check.Parameters.AddWithValue("@id", id.ToString());
        Assert.Equal((int)EstadoEmail.FalhaPermanente, Convert.ToInt32(await check.ExecuteScalarAsync()));
        Assert.Empty(await outbox.SelecionarAsync(20, default));
    }

    [MySqlIntegrationFact]
    public async Task FalhaNaOutboxReverteSolicitacao()
    {
        await fixture.LimparDadosAsync();
        var (user, _) = await NovoUsuarioAsync();
        var store = new ContaMySqlStore(fixture.ConnectionFactory, passwords);
        const FinalidadeTokenConta tipo = FinalidadeTokenConta.RedefinicaoSenha;
        await ExecutarAsync("ALTER TABLE email_outbox ADD CONSTRAINT ck_email_teste CHECK (template <> 1)");
        try
        {
            await Assert.ThrowsAsync<MySqlException>(() => store.SolicitarAsync(user.Email, null, tipo, protector, default));
            Assert.Equal(0L, await ContarAsync("SELECT COUNT(*) FROM tokens_conta"));
            Assert.Equal(0L, await ContarAsync("SELECT COUNT(*) FROM email_outbox"));
        }
        finally { await ExecutarAsync("ALTER TABLE email_outbox DROP CHECK ck_email_teste"); }
    }

    [MySqlIntegrationFact]
    public async Task TokenExpiradoNaoEnviaNemAlteraUsuario()
    {
        await fixture.LimparDadosAsync();
        var (user, repo) = await NovoUsuarioAsync();
        var accounts = new ContaMySqlStore(fixture.ConnectionFactory, passwords);
        var outbox = new EmailOutboxMySqlStore(fixture.ConnectionFactory);
        await accounts.SolicitarAsync(null, user.Id, FinalidadeTokenConta.ConfirmacaoEmail, protector, default);
        await ExecutarAsync("""
            UPDATE tokens_conta SET criado_em=UTC_TIMESTAMP(6)-INTERVAL 2 DAY,expira_em=UTC_TIMESTAMP(6)-INTERVAL 1 DAY
            """);
        Assert.Empty(await outbox.SelecionarAsync(20, default));
        Assert.Equal(1L, await ContarAsync("SELECT COUNT(*) FROM email_outbox WHERE status=5 AND token_cifrado IS NULL"));
        Assert.False((await repo.ObterPorIdAsync(user.Id))!.EmailConfirmado);
    }

    [MySqlIntegrationFact]
    public async Task CincoResultadosIndeterminadosEncerramReenvioESegredo()
    {
        await fixture.LimparDadosAsync();
        var (user, _) = await NovoUsuarioAsync();
        var accounts = new ContaMySqlStore(fixture.ConnectionFactory, passwords);
        var outbox = new EmailOutboxMySqlStore(fixture.ConnectionFactory);
        await accounts.SolicitarAsync(null, user.Id, FinalidadeTokenConta.ConfirmacaoEmail, protector, default);
        var id = Assert.Single(await outbox.SelecionarAsync(20, default));
        for (var tentativa = 1; tentativa <= 5; tentativa++)
        {
            var envio = Assert.IsType<EnvioEmail>(await outbox.AdquirirAsync(id, default));
            Assert.Equal(tentativa, envio.Tentativa);
            await outbox.FinalizarAsync(envio, EstadoEmail.Indeterminado, 2, default);
            Assert.Empty(await outbox.SelecionarAsync(20, default));
            await ExecutarAsync("UPDATE email_outbox SET proxima_tentativa_em=UTC_TIMESTAMP(6)-INTERVAL 1 SECOND");
        }
        Assert.Empty(await outbox.SelecionarAsync(20, default));
        Assert.Equal(1L, await ContarAsync("SELECT COUNT(*) FROM email_outbox WHERE status=4 AND tentativa=5 AND token_cifrado IS NULL"));
    }

    [MySqlIntegrationFact]
    public async Task JornadaCopiaSomenteNovasNotificacoesDoUsuarioSemDuplicar()
    {
        await fixture.LimparDadosAsync();
        var (user, _) = await NovoUsuarioAsync();
        await ExecutarAsync("""
            INSERT INTO notificacoes_internas(id,tipo,escopo,usuario_id,referencia_id,evento_chave,created_at,elegivel_email)
            VALUES (UUID(),0,0,@usuario,UUID(),'historico',UTC_TIMESTAMP(6),FALSE),
                   (UUID(),0,0,@usuario,UUID(),'novo',UTC_TIMESTAMP(6),TRUE),
                   (UUID(),4,1,NULL,UUID(),'admin',UTC_TIMESTAMP(6),TRUE)
            """, user.Id);
        var primeiro = new EmailOutboxMySqlStore(fixture.ConnectionFactory);
        var segundo = new EmailOutboxMySqlStore(fixture.ConnectionFactory);
        var id = Assert.Single(await primeiro.SelecionarAsync(20, default));
        Assert.Equal(id, Assert.Single(await segundo.SelecionarAsync(20, default)));
        var envio = Assert.IsType<EnvioEmail>(await primeiro.AdquirirAsync(id, default));
        Assert.Equal(TemplateEmail.Vistoria, envio.Template);
        Assert.Null(envio.Material);
        await primeiro.FinalizarAsync(envio, EstadoEmail.Enviado, null, default);
        Assert.Empty(await segundo.SelecionarAsync(20, default));
        Assert.Equal(1L, await ContarAsync("SELECT COUNT(*) FROM email_outbox"));
        Assert.Equal(3L, await ContarAsync("SELECT COUNT(*) FROM notificacoes_internas"));
    }

    [MySqlIntegrationFact]
    public async Task RelatorioFinanceiroPaginaSnapshotSemRecalcularOuAlterarSaldo()
    {
        await fixture.LimparDadosAsync();
        var (user, _) = await NovoUsuarioAsync();
        var vistoria = await JornadaFinanceiraTestData.CriarConcluidaAsync(fixture, user.Id);
        await ExecutarAsync($"""
            INSERT INTO pagamentos_vistoria(id,vistoria_id,valor,status,pago_em,created_at,updated_at)
            VALUES (UUID(),'{vistoria.Id:D}',49.95,1,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
            """);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var filtro = new FiltroRelatorio(TipoRelatorio.PagamentosRecebidos, hoje.AddDays(-1), hoje.AddDays(1), 1, 1, 1);
        var store = new RelatorioOperacionalMySqlStore(fixture.ConnectionFactory);
        var resultado = await store.ConsultarAsync(filtro, false, default);
        var exportacao = await store.ConsultarAsync(filtro, true, default);
        Assert.Equal(1L, resultado.Total);
        Assert.Equal(49.95m, resultado.ValorTotal);
        Assert.Equal(49.95m, Assert.Single(resultado.Linhas).Valor);
        Assert.Equal(Assert.Single(resultado.Linhas), Assert.Single(exportacao.Linhas));
        Assert.Empty((await store.ConsultarAsync(filtro with { Pagina = 2 }, false, default)).Linhas);
        Assert.Empty((await store.ConsultarAsync(filtro with { Status = 0 }, false, default)).Linhas);
        Assert.Equal(1L, await ContarAsync("SELECT COUNT(*) FROM pagamentos_vistoria WHERE status=1 AND valor=49.95"));
    }

    private async Task ExecutarAsync(string sql, Guid? user = null)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        if (user is not null)
            command.Parameters.AddWithValue("@usuario", user.Value.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ContarAsync(string sql)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<(Usuario User, UsuarioMySqlRepository Repo)> NovoUsuarioAsync()
    {
        var repo = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var user = new Usuario("Conta fictícia", $"{Guid.NewGuid():N}@example.invalid",
            passwords.HashPassword("ficticia-inicial-123"), tipoUsuario: TipoUsuario.Administrador);
        await repo.AdicionarAsync(user);
        return (user, repo);
    }
}

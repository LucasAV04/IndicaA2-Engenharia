using Domain.Enums;
using Infrastructure.Repositories;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UsuarioMySqlRepositoryIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task AdicionarAtualizarEObterPorIdAsync_DevePersistirEstadoEReidratarEmUtc()
    {
        await fixture.LimparDadosAsync();
        var repository = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var usuario = IntegrationTestData.CriarUsuario(tipoUsuario: TipoUsuario.Administrador);
        usuario.ConfirmarEmail();
        usuario.RegistrarLogin();
        usuario.Bloquear();

        await repository.AdicionarAsync(usuario, CancellationToken.None);
        var persistido = await repository.ObterPorIdAsync(usuario.Id, CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.True(await repository.ExistePorIdAsync(usuario.Id));
        Assert.False(await repository.ExistePorIdAsync(Guid.NewGuid()));
        Assert.Equal(usuario.Id, persistido.Id);
        Assert.Equal(usuario.Nome, persistido.Nome);
        Assert.Equal(usuario.Email, persistido.Email);
        Assert.Equal(usuario.SenhaHash, persistido.SenhaHash);
        Assert.Equal(usuario.Telefone, persistido.Telefone);
        Assert.Equal(StatusUsuario.Bloqueado, persistido.Status);
        Assert.Equal(TipoUsuario.Administrador, persistido.TipoUsuario);
        Assert.True(persistido.EmailConfirmado);
        Assert.Equal(usuario.UltimoLogin, persistido.UltimoLogin);
        Assert.Equal(DateTimeKind.Utc, persistido.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, persistido.UpdatedAt.Kind);

        usuario.AlterarNome("Nome Atualizado");
        usuario.AlterarTelefone("11888888888");
        usuario.Ativar();
        await repository.AtualizarAsync(usuario, CancellationToken.None);

        var atualizado = await repository.ObterPorIdAsync(usuario.Id, CancellationToken.None);
        Assert.NotNull(atualizado);
        Assert.Equal("Nome Atualizado", atualizado.Nome);
        Assert.Equal("11888888888", atualizado.Telefone);
        Assert.Equal(StatusUsuario.Ativo, atualizado.Status);
        Assert.Equal(usuario.UpdatedAt, atualizado.UpdatedAt, TimeSpan.FromMilliseconds(1));
    }

    [MySqlIntegrationFact]
    public async Task ConsultasDeEmailEListagemAsync_DevemRespeitarNormalizacaoEIgnorarUsuario()
    {
        await fixture.LimparDadosAsync();
        var repository = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var primeiro = IntegrationTestData.CriarUsuario("primeiro@exemplo.com");
        var segundo = IntegrationTestData.CriarUsuario("segundo@exemplo.com");
        await repository.AdicionarAsync(primeiro, CancellationToken.None);
        await repository.AdicionarAsync(segundo, CancellationToken.None);

        var porEmail = await repository.ObterPorEmailAsync("PRIMEIRO@EXEMPLO.COM", CancellationToken.None);
        var inexistente = await repository.ObterPorEmailAsync("ausente@exemplo.com", CancellationToken.None);
        var todos = await repository.ObterTodosAsync(CancellationToken.None);

        Assert.NotNull(porEmail);
        Assert.Equal(primeiro.Id, porEmail.Id);
        Assert.Null(inexistente);
        Assert.True(await repository.ExistePorEmailAsync(primeiro.Email, cancellationToken: CancellationToken.None));
        Assert.False(await repository.ExistePorEmailAsync("ausente@exemplo.com", cancellationToken: CancellationToken.None));
        Assert.False(await repository.ExistePorEmailAsync(primeiro.Email, primeiro.Id, CancellationToken.None));
        Assert.True(await repository.ExistePorEmailAsync(primeiro.Email, segundo.Id, CancellationToken.None));
        Assert.Contains(todos, item => item.Id == primeiro.Id);
        Assert.Contains(todos, item => item.Id == segundo.Id);
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_ComEmailDuplicado_DeveRespeitarConstraintUniqueDoMySql()
    {
        await fixture.LimparDadosAsync();
        var repository = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        await repository.AdicionarAsync(IntegrationTestData.CriarUsuario("unico@exemplo.com"), CancellationToken.None);

        await Assert.ThrowsAsync<MySqlException>(() =>
            repository.AdicionarAsync(IntegrationTestData.CriarUsuario("unico@exemplo.com"), CancellationToken.None));
    }
}

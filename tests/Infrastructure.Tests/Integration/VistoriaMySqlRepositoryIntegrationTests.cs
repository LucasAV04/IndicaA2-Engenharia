using Domain.Enums;
using Infrastructure.Repositories;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class VistoriaMySqlRepositoryIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task AdicionarEObterPorIdAsync_DevePreservarDecimalDataDeNegocioEReidratacao()
    {
        await fixture.LimparDadosAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var repository = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var usuario = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(usuario, CancellationToken.None);
        var vistoria = IntegrationTestData.CriarVistoria(usuario.Id, 72.35m);

        await repository.AdicionarAsync(vistoria, CancellationToken.None);
        var persistida = await repository.ObterPorIdAsync(vistoria.Id, CancellationToken.None);

        Assert.NotNull(persistida);
        Assert.Equal(vistoria.Id, persistida.Id);
        Assert.Equal(usuario.Id, persistida.UsuarioId);
        Assert.Equal("Apartamento", persistida.TipoPlanta);
        Assert.Equal(72.35m, persistida.AreaM2);
        Assert.Equal(PacoteVistoria.Total, persistida.Pacote);
        Assert.Equal(vistoria.DataAgendada, persistida.DataAgendada);
        Assert.Equal(StatusVistoria.Agendada, persistida.Status);
        Assert.Equal(DateTimeKind.Unspecified, persistida.DataAgendada.Kind);
        Assert.Equal(DateTimeKind.Utc, persistida.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, persistida.UpdatedAt.Kind);
    }

    [MySqlIntegrationFact]
    public async Task ConsultasEAtualizacaoAsync_DevemFiltrarPorUsuarioEPersistirTransicao()
    {
        await fixture.LimparDadosAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var repository = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var primeiroUsuario = IntegrationTestData.CriarUsuario();
        var segundoUsuario = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(primeiroUsuario, CancellationToken.None);
        await usuarios.AdicionarAsync(segundoUsuario, CancellationToken.None);
        var primeira = IntegrationTestData.CriarVistoria(primeiroUsuario.Id);
        var segunda = IntegrationTestData.CriarVistoria(segundoUsuario.Id, 85.50m);
        await repository.AdicionarAsync(primeira, CancellationToken.None);
        await repository.AdicionarAsync(segunda, CancellationToken.None);

        primeira.MarcarRealizada();
        await repository.AtualizarAsync(primeira, CancellationToken.None);
        var porUsuario = await repository.ObterPorUsuarioIdAsync(primeiroUsuario.Id, CancellationToken.None);
        var todas = await repository.ObterTodasAsync(CancellationToken.None);
        var inexistente = await repository.ObterPorIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Single(porUsuario);
        Assert.Equal(StatusVistoria.Realizada, porUsuario.Single().Status);
        Assert.Equal(primeira.UpdatedAt, porUsuario.Single().UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, todas.Count);
        Assert.Null(inexistente);
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_ComUsuarioInexistente_DeveRespeitarFkDoMySql()
    {
        await fixture.LimparDadosAsync();
        var repository = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var vistoria = IntegrationTestData.CriarVistoria(Guid.NewGuid());

        await Assert.ThrowsAsync<MySqlException>(() => repository.AdicionarAsync(vistoria, CancellationToken.None));
    }
}

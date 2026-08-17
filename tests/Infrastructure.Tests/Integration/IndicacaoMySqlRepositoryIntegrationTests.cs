using Domain.Entities;
using Domain.Enums;
using Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IndicacaoMySqlRepositoryIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task AdicionarAtualizarEObterPorIdAsync_DevePersistirVinculosStatusEDatas()
    {
        await fixture.LimparDadosAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistorias = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var indicacoes = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var indicador = IntegrationTestData.CriarUsuario();
        var indicado = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(indicador, CancellationToken.None);
        await usuarios.AdicionarAsync(indicado, CancellationToken.None);
        var vistoria = IntegrationTestData.CriarVistoria(indicado.Id);
        await vistorias.AdicionarAsync(vistoria, CancellationToken.None);
        var indicacao = new Indicacao(indicador.Id, "Ana Indicada", "11999999999", "a2-123");
        indicacao.VincularUsuarioIndicado(indicado.Id);
        indicacao.VincularVistoria(vistoria.Id);

        await indicacoes.AdicionarAsync(indicacao, CancellationToken.None);
        var persistida = await indicacoes.ObterPorIdAsync(indicacao.Id, CancellationToken.None);

        Assert.NotNull(persistida);
        Assert.Equal(indicacao.Id, persistida.Id);
        Assert.Equal(indicador.Id, persistida.UsuarioIndicadorId);
        Assert.Equal(indicado.Id, persistida.UsuarioIndicadoId);
        Assert.Equal("Ana Indicada", persistida.NomeIndicada);
        Assert.Equal("11999999999", persistida.TelefoneIndicada);
        Assert.Equal("A2-123", persistida.CodigoIndicacaoUsado);
        Assert.Equal(vistoria.Id, persistida.VistoriaId);
        Assert.Equal(StatusIndicacao.VistoriaVinculada, persistida.Status);
        Assert.Equal(DateTimeKind.Utc, persistida.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, persistida.UpdatedAt.Kind);

        indicacao.Cancelar();
        await indicacoes.AtualizarAsync(indicacao, CancellationToken.None);
        var atualizada = await indicacoes.ObterPorIdAsync(indicacao.Id, CancellationToken.None);
        Assert.NotNull(atualizada);
        Assert.Equal(StatusIndicacao.Cancelada, atualizada.Status);
        Assert.Equal(vistoria.Id, atualizada.VistoriaId);
    }

    [MySqlIntegrationFact]
    public async Task FiltrosEConsultasAsync_DevemRetornarSomenteIndicacoesCorrespondentes()
    {
        await fixture.LimparDadosAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var repository = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var primeiroIndicador = IntegrationTestData.CriarUsuario();
        var segundoIndicador = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(primeiroIndicador, CancellationToken.None);
        await usuarios.AdicionarAsync(segundoIndicador, CancellationToken.None);
        var pendente = new Indicacao(primeiroIndicador.Id, "Pendente", "11999999991", "A2-001");
        var cancelada = new Indicacao(primeiroIndicador.Id, "Cancelada", "11999999992", "A2-002");
        cancelada.Cancelar();
        var deOutroUsuario = new Indicacao(segundoIndicador.Id, "Outra", "11999999993", "A2-003");
        await repository.AdicionarAsync(pendente, CancellationToken.None);
        await repository.AdicionarAsync(cancelada, CancellationToken.None);
        await repository.AdicionarAsync(deOutroUsuario, CancellationToken.None);

        var porIndicador = await repository.ObterPorUsuarioIndicadorIdAsync(primeiroIndicador.Id, CancellationToken.None);
        var porStatus = await repository.ObterPorStatusAsync(StatusIndicacao.Cancelada, CancellationToken.None);
        var todas = await repository.ObterTodasAsync(CancellationToken.None);
        var inexistente = await repository.ObterPorIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, porIndicador.Count);
        Assert.All(porIndicador, item => Assert.Equal(primeiroIndicador.Id, item.UsuarioIndicadorId));
        Assert.Single(porStatus);
        Assert.Equal(cancelada.Id, porStatus.Single().Id);
        Assert.Equal(3, todas.Count);
        Assert.Null(inexistente);
    }

    [MySqlIntegrationFact]
    public async Task CodigoIndicacaoDoUsuario_DeveSerConsultavelEPreservadoComoSnapshotNaIndicacao()
    {
        await fixture.LimparDadosAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var indicacoes = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var indicador = IntegrationTestData.CriarUsuario(codigoIndicacao: "A1B2C3D4");
        await usuarios.AdicionarAsync(indicador, CancellationToken.None);

        var resolvido = await usuarios.ObterPorCodigoIndicacaoAsync("  a1b2c3d4 ", CancellationToken.None);
        Assert.NotNull(resolvido);
        Assert.Equal(indicador.Id, resolvido.Id);
        Assert.Equal("A1B2C3D4", resolvido.CodigoIndicacao);

        var indicacao = new Indicacao(
            resolvido.Id,
            "Ana Indicada",
            "11999999999",
            resolvido.CodigoIndicacao!);
        await indicacoes.AdicionarAsync(indicacao, CancellationToken.None);

        var persistida = await indicacoes.ObterPorIdAsync(indicacao.Id, CancellationToken.None);
        Assert.NotNull(persistida);
        Assert.Equal(resolvido.Id, persistida.UsuarioIndicadorId);
        Assert.Equal("A1B2C3D4", persistida.CodigoIndicacaoUsado);
    }
}

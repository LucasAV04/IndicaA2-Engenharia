using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IndicacaoVistoriaMySqlRepositoryIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task ObterPorVistoriaIdAsync_QuandoExisteVinculo_DeveRetornarIndicacaoECancellationToken()
    {
        await fixture.LimparDadosAsync();
        var (indicacoes, indicacao, vistoria) = await CriarIndicacaoComVistoriaAsync();
        var cancellationToken = new CancellationTokenSource().Token;

        var encontrada = await indicacoes.ObterPorVistoriaIdAsync(vistoria.Id, cancellationToken);

        Assert.NotNull(encontrada);
        Assert.Equal(indicacao.Id, encontrada.Id);
        Assert.Equal(vistoria.Id, encontrada.VistoriaId);
        Assert.Equal(indicacao.UsuarioIndicadorId, encontrada.UsuarioIndicadorId);
    }

    [MySqlIntegrationFact]
    public async Task ObterPorVistoriaIdAsync_QuandoVistoriaNaoPossuiIndicacao_DeveRetornarNulo()
    {
        await fixture.LimparDadosAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistorias = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var indicacoes = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var usuario = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(usuario, CancellationToken.None);
        var vistoria = IntegrationTestData.CriarVistoria(usuario.Id);
        await vistorias.AdicionarAsync(vistoria, CancellationToken.None);

        var encontrada = await indicacoes.ObterPorVistoriaIdAsync(vistoria.Id, CancellationToken.None);

        Assert.Null(encontrada);
    }

    [MySqlIntegrationFact]
    public async Task AtualizarAsync_QuandoVistoriaJaEstiverVinculadaOutraIndicacao_DeveTraduzirConstraintEspecifica()
    {
        await fixture.LimparDadosAsync();
        var (indicacoes, _, vistoria) = await CriarIndicacaoComVistoriaAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var segundoIndicador = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(segundoIndicador, CancellationToken.None);
        var segundaIndicacao = new Indicacao(segundoIndicador.Id, "Outra indicada", "11999999998", "A2-456");
        segundaIndicacao.VincularUsuarioIndicado(vistoria.UsuarioId);
        await indicacoes.AdicionarAsync(segundaIndicacao, CancellationToken.None);
        segundaIndicacao.VincularVistoria(vistoria.Id);

        await Assert.ThrowsAsync<VistoriaJaVinculadaOutraIndicacaoException>(() =>
            indicacoes.AtualizarAsync(segundaIndicacao, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoIndicacoesSemVistoria_DevePermitirMultiplosValoresNulos()
    {
        await fixture.LimparDadosAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var indicacoes = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var indicador = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(indicador, CancellationToken.None);
        var primeira = new Indicacao(indicador.Id, "Indicada A", "11999999991", "A2-001");
        var segunda = new Indicacao(indicador.Id, "Indicada B", "11999999992", "A2-002");

        await indicacoes.AdicionarAsync(primeira, CancellationToken.None);
        await indicacoes.AdicionarAsync(segunda, CancellationToken.None);

        Assert.Null(primeira.VistoriaId);
        Assert.Null(segunda.VistoriaId);
        Assert.Equal(2, (await indicacoes.ObterTodasAsync(CancellationToken.None)).Count);
    }

    private async Task<(IndicacaoMySqlRepository Indicacoes, Indicacao Indicacao, Vistoria Vistoria)> CriarIndicacaoComVistoriaAsync()
    {
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistorias = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var indicacoes = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var indicador = IntegrationTestData.CriarUsuario();
        var indicado = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(indicador, CancellationToken.None);
        await usuarios.AdicionarAsync(indicado, CancellationToken.None);
        var vistoria = IntegrationTestData.CriarVistoria(indicado.Id);
        await vistorias.AdicionarAsync(vistoria, CancellationToken.None);
        var indicacao = new Indicacao(indicador.Id, "Ana Indicada", "11999999999", "A2-123");
        indicacao.VincularUsuarioIndicado(indicado.Id);
        indicacao.VincularVistoria(vistoria.Id);
        await indicacoes.AdicionarAsync(indicacao, CancellationToken.None);

        return (indicacoes, indicacao, vistoria);
    }
}

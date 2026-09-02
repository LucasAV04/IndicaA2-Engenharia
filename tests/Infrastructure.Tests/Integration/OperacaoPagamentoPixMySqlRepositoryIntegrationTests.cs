using Domain.Entities;
using Domain.Enums;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Infrastructure.Security;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OperacaoPagamentoPixMySqlRepositoryIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task AdicionarEObterAbertasAsync_DevePersistirEnvioSemDadosPixSensiveis()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var repository = CriarRepository();
        var operacao = OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1);

        await repository.AdicionarAsync(operacao, CancellationToken.None);

        var aberta = Assert.Single(await repository.ObterAbertasAsync(CancellationToken.None));
        Assert.Equal(operacao.Id, aberta.Id);
        Assert.Equal(pagamentoPix.Id.ToString("N"), aberta.ReferenciaIdempotente);
        Assert.Equal(TipoOperacaoPagamentoPix.Envio, aberta.TipoOperacao);
        Assert.Equal(1, aberta.NumeroTentativaEnvio);
        Assert.Null(aberta.Resultado);
    }

    [MySqlIntegrationFact]
    public async Task FinalizarAsync_QuandoDoisExecutoresConcorrerem_DevePreservarPrimeiraFinalizacao()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var repository = CriarRepository();
        var operacao = OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1);
        await repository.AdicionarAsync(operacao, CancellationToken.None);

        var primeira = (await repository.ObterPorIdAsync(operacao.Id, CancellationToken.None))!;
        var segunda = (await repository.ObterPorIdAsync(operacao.Id, CancellationToken.None))!;
        primeira.Finalizar(ResultadoOperacaoPagamentoPix.Confirmado, "id-um", "codigo-um");
        segunda.Finalizar(ResultadoOperacaoPagamentoPix.FalhaConfirmada, "id-dois", "codigo-dois");

        var resultados = await Task.WhenAll(
            CriarRepository().FinalizarAsync(primeira, CancellationToken.None),
            CriarRepository().FinalizarAsync(segunda, CancellationToken.None));

        Assert.Equal(1, resultados.Count(resultado => resultado));
        var persistida = (await repository.ObterPorIdAsync(operacao.Id, CancellationToken.None))!;
        Assert.True(persistida.FinishedAt.HasValue);
        Assert.Contains(persistida.Resultado!.Value, new[]
        {
            ResultadoOperacaoPagamentoPix.Confirmado,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada
        });
        Assert.Empty(await repository.ObterAbertasAsync(CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task ConsultasParaMesmoPagamento_DevePermitirHistoricoEOrdenarCronologicamente()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var repository = CriarRepository();
        var primeira = OperacaoPagamentoPix.IniciarConsulta(pagamentoPix.Id);
        await repository.AdicionarAsync(primeira, CancellationToken.None);
        var segunda = OperacaoPagamentoPix.IniciarConsulta(pagamentoPix.Id);
        await repository.AdicionarAsync(segunda, CancellationToken.None);

        var operacoes = (await repository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None)).ToArray();

        Assert.Equal(2, operacoes.Length);
        Assert.All(operacoes, operacao => Assert.Null(operacao.NumeroTentativaEnvio));
        Assert.True(operacoes[0].CreatedAt <= operacoes[1].CreatedAt);
    }

    private async Task<PagamentoPix> CriarPagamentoPixPersistidoAsync()
    {
        var usuarioRepository = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistoriaRepository = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var indicacaoRepository = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var pagamentoVistoriaRepository = new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        var cashbackRepository = new CashbackMySqlRepository(fixture.ConnectionFactory);
        var indicador = IntegrationTestData.CriarUsuario();
        var indicada = IntegrationTestData.CriarUsuario();
        await usuarioRepository.AdicionarAsync(indicador, CancellationToken.None);
        await usuarioRepository.AdicionarAsync(indicada, CancellationToken.None);
        var vistoria = IntegrationTestData.CriarVistoria(indicada.Id);
        await vistoriaRepository.AdicionarAsync(vistoria, CancellationToken.None);
        var indicacao = new Indicacao(indicador.Id, "Indicada Auditoria", "11999999999", indicador.CodigoIndicacao!);
        indicacao.VincularVistoria(vistoria.Id);
        await indicacaoRepository.AdicionarAsync(indicacao, CancellationToken.None);
        var pagamentoVistoria = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id);
        pagamentoVistoria.Confirmar();
        await pagamentoVistoriaRepository.AdicionarAsync(pagamentoVistoria, CancellationToken.None);
        var cashback = Cashback.Criar(indicacao.Id, pagamentoVistoria.Id, indicador.Id, pagamentoVistoria.Valor);
        cashback.Aprovar();
        await cashbackRepository.AdicionarAsync(cashback, CancellationToken.None);
        var pagamentoPix = PagamentoPix.Criar(cashback.Id, indicador.Id, cashback.Valor, TipoChavePix.Email, "snapshot@exemplo.com");
        await new PagamentoPixMySqlRepository(fixture.ConnectionFactory, new AesGcmDadosPixProtector(CriarChave())).AdicionarAsync(pagamentoPix, CancellationToken.None);
        return pagamentoPix;
    }

    private OperacaoPagamentoPixMySqlRepository CriarRepository() => new(fixture.ConnectionFactory);

    private static string CriarChave() => Convert.ToBase64String(Enumerable.Range(1, 32).Select(valor => (byte)valor).ToArray());
}

using Application.Interfaces.Providers;
using Application.Models;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Repositories;
using Infrastructure.Security;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PagamentoPixReconciliacaoServiceIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoProviderConfirmar_DeveFinalizarConsultaEEnvioAbertoSemAlterarPagamento()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync();
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var envioAberto = OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1);
        await operacaoRepository.AdicionarAsync(envioAberto, CancellationToken.None);
        var materialAntes = await ObterMaterialProtegidoAsync(pagamentoPix.Id);
        var provider = new PixProviderFake(PixProviderResult.Confirmado("provider-id", "provider-code"));

        var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
        var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);

        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
        Assert.True(resultado.OperacaoEnvioAbertaResolvida);
        Assert.Equal(1, provider.QuantidadeConsultas);
        Assert.Equal(0, provider.QuantidadeEnvios);
        Assert.Equal(StatusPagamentoPix.Processando, pagamentoPersistido.Status);
        Assert.Equal(1, pagamentoPersistido.QuantidadeTentativas);
        Assert.Equal(materialAntes, await ObterMaterialProtegidoAsync(pagamentoPix.Id));
        Assert.Equal(2, operacoes.Count);
        Assert.All(operacoes, operacao => Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, operacao.Resultado));
        var consulta = Assert.Single(
            operacoes,
            operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta);
        Assert.Null(consulta.NumeroTentativaEnvio);
        Assert.Equal(pagamentoPix.Id.ToString("N"), consulta.ReferenciaIdempotente);
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoProviderRetornarPendente_DeveManterEnvioAbertoEAuditarConsulta()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync();
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var envioAberto = OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1);
        await operacaoRepository.AdicionarAsync(envioAberto, CancellationToken.None);
        var provider = new PixProviderFake(PixProviderResult.Pendente());

        var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

        var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);
        var envio = Assert.Single(
            operacoes,
            operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio);
        var consulta = Assert.Single(
            operacoes,
            operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta);

        Assert.False(resultado.OperacaoEnvioAbertaResolvida);
        Assert.False(envio.FinishedAt.HasValue);
        Assert.True(consulta.FinishedAt.HasValue);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Pendente, consulta.Resultado);
        Assert.Equal(1, provider.QuantidadeConsultas);
        Assert.Equal(0, provider.QuantidadeEnvios);
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoConsultaAnteriorEstiverAberta_DeveRegistrarNovaConsulta()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync();
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await operacaoRepository.AdicionarAsync(
            OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1),
            CancellationToken.None);
        var consultaAnterior = OperacaoPagamentoPix.IniciarConsulta(pagamentoPix.Id);
        await operacaoRepository.AdicionarAsync(consultaAnterior, CancellationToken.None);
        var provider = new PixProviderFake(PixProviderResult.Indeterminado());

        await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

        var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);
        var consultas = operacoes.Where(operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta).ToArray();

        Assert.Equal(2, consultas.Length);
        Assert.False(consultas.Single(operacao => operacao.Id == consultaAnterior.Id).FinishedAt.HasValue);
        Assert.Single(
            consultas,
            operacao => operacao.Id != consultaAnterior.Id && operacao.FinishedAt.HasValue);
        Assert.Equal(1, provider.QuantidadeConsultas);
        Assert.Equal(0, provider.QuantidadeEnvios);
    }

    private PagamentoPixReconciliacaoService CriarService(IPixProvider provider) =>
        new(
            CriarPagamentoRepository(),
            new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory),
            provider);

    private async Task<PagamentoPix> CriarPagamentoPixProcessandoAsync()
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
        var indicacao = new Indicacao(indicador.Id, "Indicada Reconciliação", "11999999999", indicador.CodigoIndicacao!);
        indicacao.VincularVistoria(vistoria.Id);
        await indicacaoRepository.AdicionarAsync(indicacao, CancellationToken.None);
        var pagamentoVistoria = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id);
        pagamentoVistoria.Confirmar();
        await pagamentoVistoriaRepository.AdicionarAsync(pagamentoVistoria, CancellationToken.None);
        var cashback = Cashback.Criar(indicacao.Id, pagamentoVistoria.Id, indicador.Id, pagamentoVistoria.Valor);
        cashback.Aprovar();
        await cashbackRepository.AdicionarAsync(cashback, CancellationToken.None);
        var pagamentoPix = PagamentoPix.Criar(
            cashback.Id,
            indicador.Id,
            cashback.Valor,
            TipoChavePix.Email,
            "snapshot@exemplo.com");
        pagamentoPix.IniciarTentativa();
        await CriarPagamentoRepository().AdicionarAsync(pagamentoPix, CancellationToken.None);
        return pagamentoPix;
    }

    private PagamentoPixMySqlRepository CriarPagamentoRepository() =>
        new(fixture.ConnectionFactory, new AesGcmDadosPixProtector(CriarChave()));

    private async Task<string> ObterMaterialProtegidoAsync(Guid pagamentoPixId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            """
            SELECT CONCAT(
                HEX(chave_pix_ciphertext), ':', HEX(chave_pix_nonce), ':',
                HEX(chave_pix_tag), ':', encryption_version)
            FROM pagamentos_pix
            WHERE id = @id;
            """,
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = pagamentoPixId.ToString();
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static string CriarChave() =>
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(valor => (byte)valor).ToArray());

    private sealed class PixProviderFake(PixProviderResult result) : IPixProvider
    {
        private int _quantidadeConsultas;
        private int _quantidadeEnvios;

        public int QuantidadeConsultas => _quantidadeConsultas;
        public int QuantidadeEnvios => _quantidadeEnvios;

        public Task<PixProviderResult> EnviarAsync(
            PixEnvioRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _quantidadeEnvios);
            throw new InvalidOperationException("O envio não é permitido durante a reconciliação.");
        }

        public Task<PixProviderResult> ConsultarAsync(
            PixConsultaRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _quantidadeConsultas);
            return Task.FromResult(result);
        }
    }
}

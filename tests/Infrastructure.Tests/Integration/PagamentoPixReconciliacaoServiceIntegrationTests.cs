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

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoTentativaAnteriorFalhou_DeveConsultarESomenteResolverEnvioAtual()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync(2);
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var inicio = DateTime.UtcNow.AddMinutes(-3);
        var envioAnterior = CriarOperacao(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 1,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada, inicio);
        var envioAtual = CriarOperacao(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 2, null, inicio.AddMinutes(1));
        await operacaoRepository.AdicionarAsync(envioAnterior, CancellationToken.None);
        await operacaoRepository.AdicionarAsync(envioAtual, CancellationToken.None);
        var materialAntes = await ObterMaterialProtegidoAsync(pagamentoPix.Id);
        var provider = new PixProviderFake(PixProviderResult.Confirmado());

        var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
        var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);

        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
        Assert.Equal(1, provider.QuantidadeConsultas);
        Assert.Equal(0, provider.QuantidadeEnvios);
        Assert.Equal(StatusPagamentoPix.Processando, pagamentoPersistido.Status);
        Assert.Equal(2, pagamentoPersistido.QuantidadeTentativas);
        Assert.Equal(materialAntes, await ObterMaterialProtegidoAsync(pagamentoPix.Id));
        Assert.Equal(ResultadoOperacaoPagamentoPix.FalhaConfirmada,
            operacoes.Single(operacao => operacao.Id == envioAnterior.Id).Resultado);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado,
            operacoes.Single(operacao => operacao.Id == envioAtual.Id).Resultado);
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoConsultaConclusivaForAnteriorAoEnvioAtual_DeveIgnoraLa()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync(2);
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var inicio = DateTime.UtcNow.AddMinutes(-4);
        var envioAnterior = CriarOperacao(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 1,
            ResultadoOperacaoPagamentoPix.Pendente, inicio);
        var consultaAnterior = CriarOperacao(pagamentoPix.Id, TipoOperacaoPagamentoPix.Consulta, null,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada, inicio.AddMinutes(1));
        var envioAtual = CriarOperacao(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 2, null, inicio.AddMinutes(2));
        await operacaoRepository.AdicionarAsync(envioAnterior, CancellationToken.None);
        await operacaoRepository.AdicionarAsync(consultaAnterior, CancellationToken.None);
        await operacaoRepository.AdicionarAsync(envioAtual, CancellationToken.None);
        var provider = new PixProviderFake(PixProviderResult.Pendente());

        var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

        var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);
        Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, resultado.Status);
        Assert.Equal(1, provider.QuantidadeConsultas);
        Assert.Equal(0, provider.QuantidadeEnvios);
        Assert.Equal(ResultadoOperacaoPagamentoPix.FalhaConfirmada,
            operacoes.Single(operacao => operacao.Id == consultaAnterior.Id).Resultado);
        Assert.Equal(2, operacoes.Count(operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta));
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoEnvioAtualForConclusivo_NaoDeveConsultar()
    {
        foreach (var resultadoConclusivo in new[]
                 {
                     ResultadoOperacaoPagamentoPix.Confirmado,
                     ResultadoOperacaoPagamentoPix.FalhaConfirmada
                 })
        {
            await fixture.LimparDadosAsync();
            var pagamentoPix = await CriarPagamentoPixProcessandoAsync();
            var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
            var envioAtual = CriarOperacao(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 1,
                resultadoConclusivo, DateTime.UtcNow.AddMinutes(-1));
            await operacaoRepository.AdicionarAsync(envioAtual, CancellationToken.None);
            var provider = new PixProviderFake(PixProviderResult.Pendente());

            var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

            Assert.Equal(StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo, resultado.Status);
            Assert.Equal(resultadoConclusivo, resultado.ResultadoOperacao);
            Assert.Equal(0, provider.QuantidadeConsultas);
            Assert.Equal(0, provider.QuantidadeEnvios);
        }
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoConsultaDoCicloAtualForConclusiva_NaoDeveConsultarNovamente()
    {
        foreach (var resultadoConclusivo in new[]
                 {
                     ResultadoOperacaoPagamentoPix.Confirmado,
                     ResultadoOperacaoPagamentoPix.FalhaConfirmada
                 })
        {
            await fixture.LimparDadosAsync();
            var pagamentoPix = await CriarPagamentoPixProcessandoAsync();
            var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
            var inicio = DateTime.UtcNow.AddMinutes(-2);
            var envioAtual = CriarOperacao(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 1,
                ResultadoOperacaoPagamentoPix.Pendente, inicio);
            var consultaAtual = CriarOperacao(pagamentoPix.Id, TipoOperacaoPagamentoPix.Consulta, null,
                resultadoConclusivo, inicio.AddMinutes(1));
            await operacaoRepository.AdicionarAsync(envioAtual, CancellationToken.None);
            await operacaoRepository.AdicionarAsync(consultaAtual, CancellationToken.None);
            var provider = new PixProviderFake(PixProviderResult.Pendente());

            var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

            Assert.Equal(StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo, resultado.Status);
            Assert.Equal(resultadoConclusivo, resultado.ResultadoOperacao);
            Assert.Equal(0, provider.QuantidadeConsultas);
            Assert.Equal(0, provider.QuantidadeEnvios);
        }
    }

    private PagamentoPixReconciliacaoService CriarService(IPixProvider provider) =>
        new(
            CriarPagamentoRepository(),
            new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory),
            provider);

    private async Task<PagamentoPix> CriarPagamentoPixProcessandoAsync(int quantidadeTentativas = 1)
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
        if (quantidadeTentativas > 1)
        {
            pagamentoPix = PagamentoPix.Reidratar(
                pagamentoPix.Id,
                pagamentoPix.CashbackId,
                pagamentoPix.UsuarioBeneficiarioId,
                pagamentoPix.Valor,
                pagamentoPix.TipoChavePix,
                pagamentoPix.ChavePix,
                StatusPagamentoPix.Processando,
                quantidadeTentativas,
                pagamentoPix.CreatedAt,
                pagamentoPix.UpdatedAt);
        }
        await CriarPagamentoRepository().AdicionarAsync(pagamentoPix, CancellationToken.None);
        return pagamentoPix;
    }

    private static OperacaoPagamentoPix CriarOperacao(
        Guid pagamentoPixId,
        TipoOperacaoPagamentoPix tipoOperacao,
        int? numeroTentativaEnvio,
        ResultadoOperacaoPagamentoPix? resultado,
        DateTime createdAt) =>
        OperacaoPagamentoPix.Reidratar(
            Guid.NewGuid(),
            pagamentoPixId,
            tipoOperacao,
            numeroTentativaEnvio,
            pagamentoPixId.ToString("N"),
            resultado,
            resultado.HasValue ? "provider-id" : null,
            resultado.HasValue ? "provider-code" : null,
            createdAt,
            resultado.HasValue ? createdAt.AddSeconds(1) : createdAt,
            resultado.HasValue ? createdAt.AddSeconds(1) : null);

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

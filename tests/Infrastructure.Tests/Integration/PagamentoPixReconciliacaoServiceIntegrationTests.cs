using Application.Interfaces.Providers;
using Application.Models;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
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
    public async Task ReconciliarAsync_QuandoConsultaDoCicloAtualEstiverAberta_NaoDeveRegistrarNovaConsulta()
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

        var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

        var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);
        var consultas = operacoes.Where(operacao => operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta).ToArray();

        Assert.Equal(StatusReconciliacaoPagamentoPix.ConsultaEmAndamento, resultado.Status);
        Assert.Single(consultas);
        Assert.False(consultas.Single(operacao => operacao.Id == consultaAnterior.Id).FinishedAt.HasValue);
        Assert.Equal(0, provider.QuantidadeConsultas);
        Assert.Equal(0, provider.QuantidadeEnvios);
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoTentativaAnteriorFalhou_DeveConsultarESomenteResolverEnvioAtual()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync(2);
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var inicio = DateTime.UtcNow.AddMinutes(-3);
        var envioAnterior = CriarOperacaoAberta(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 1, inicio);
        var envioAtual = CriarOperacaoAberta(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 2, inicio.AddMinutes(1));
        await AdicionarEFinalizarAsync(operacaoRepository, envioAnterior, ResultadoOperacaoPagamentoPix.FalhaConfirmada);
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
        var envioAnterior = CriarOperacaoAberta(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 1, inicio);
        var consultaAnterior = CriarOperacaoAberta(pagamentoPix.Id, TipoOperacaoPagamentoPix.Consulta, null, inicio.AddMinutes(1));
        var envioAtual = CriarOperacaoAberta(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 2, inicio.AddMinutes(2));
        await AdicionarEFinalizarAsync(operacaoRepository, envioAnterior, ResultadoOperacaoPagamentoPix.Pendente);
        await AdicionarEFinalizarAsync(operacaoRepository, consultaAnterior, ResultadoOperacaoPagamentoPix.FalhaConfirmada);
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
            var envioAtual = CriarOperacaoAberta(
                pagamentoPix.Id,
                TipoOperacaoPagamentoPix.Envio,
                1,
                DateTime.UtcNow.AddMinutes(-1));
            await AdicionarEFinalizarAsync(operacaoRepository, envioAtual, resultadoConclusivo);
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
            var envioAtual = CriarOperacaoAberta(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 1, inicio);
            var consultaAtual = CriarOperacaoAberta(
                pagamentoPix.Id,
                TipoOperacaoPagamentoPix.Consulta,
                null,
                inicio.AddMinutes(1));
            await AdicionarEFinalizarAsync(operacaoRepository, envioAtual, ResultadoOperacaoPagamentoPix.Pendente);
            await AdicionarEFinalizarAsync(operacaoRepository, consultaAtual, resultadoConclusivo);
            var provider = new PixProviderFake(PixProviderResult.Pendente());

            var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

            Assert.Equal(StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo, resultado.Status);
            Assert.Equal(resultadoConclusivo, resultado.ResultadoOperacao);
            Assert.Equal(0, provider.QuantidadeConsultas);
            Assert.Equal(0, provider.QuantidadeEnvios);
        }
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoConsultaConclusivaExistir_DeveRecuperarEnvioAtualAbertoSemConsultarProvider()
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
            var envioAtual = CriarOperacaoAberta(pagamentoPix.Id, TipoOperacaoPagamentoPix.Envio, 1, inicio);
            var consultaAtual = CriarOperacaoAberta(
                pagamentoPix.Id,
                TipoOperacaoPagamentoPix.Consulta,
                null,
                inicio.AddMinutes(1));
            await operacaoRepository.AdicionarAsync(envioAtual, CancellationToken.None);
            await AdicionarEFinalizarAsync(operacaoRepository, consultaAtual, resultadoConclusivo);
            var materialAntes = await ObterMaterialProtegidoAsync(pagamentoPix.Id);
            var cashbackAntes = await ObterSnapshotCashbackAsync(pagamentoPix.CashbackId);
            var provider = new PixProviderFake(PixProviderResult.Pendente());

            var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

            var pagamentoPersistido = (await CriarPagamentoRepository()
                .ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
            var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);
            var envioPersistido = operacoes.Single(operacao => operacao.Id == envioAtual.Id);
            var consultaPersistida = operacoes.Single(operacao => operacao.Id == consultaAtual.Id);

            Assert.Equal(StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo, resultado.Status);
            Assert.Equal(resultadoConclusivo, resultado.ResultadoOperacao);
            Assert.Equal(0, provider.QuantidadeConsultas);
            Assert.Equal(0, provider.QuantidadeEnvios);
            Assert.Equal(resultadoConclusivo, envioPersistido.Resultado);
            Assert.Equal(consultaPersistida.IdentificadorProvider, envioPersistido.IdentificadorProvider);
            Assert.Equal(consultaPersistida.Codigo, envioPersistido.Codigo);
            Assert.Equal(resultadoConclusivo, consultaPersistida.Resultado);
            Assert.Equal(2, operacoes.Count);
            Assert.Equal(StatusPagamentoPix.Processando, pagamentoPersistido.Status);
            Assert.Equal(1, pagamentoPersistido.QuantidadeTentativas);
            Assert.Equal(materialAntes, await ObterMaterialProtegidoAsync(pagamentoPix.Id));
            Assert.Equal(cashbackAntes, await ObterSnapshotCashbackAsync(pagamentoPix.CashbackId));
        }
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoConsultaForPreparada_DeveImpedirAplicacaoAteFinalizacaoDaAuditoria()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync();
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await operacaoRepository.AdicionarAsync(
            OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1),
            CancellationToken.None);
        var provider = new PixProviderBloqueavel(PixProviderResult.Confirmado());
        var reconciliacao = CriarService(provider);
        var aplicacao = new PagamentoPixAplicacaoResultadoService(
            CriarPagamentoRepository(),
            new CashbackMySqlRepository(fixture.ConnectionFactory),
            new PagamentoPixAplicacaoResultadoMySqlStore(fixture.ConnectionFactory));

        var tarefaReconciliacao = reconciliacao.ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);
        await provider.ConsultaIniciada;

        var duranteConsulta = await aplicacao.AplicarAsync(pagamentoPix.Id, CancellationToken.None);
        Assert.Equal(StatusAplicacaoPagamentoPix.RequerReconciliacao, duranteConsulta.Status);

        provider.LiberarConsulta();
        _ = await tarefaReconciliacao;

        var aposReconciliacao = await aplicacao.AplicarAsync(pagamentoPix.Id, CancellationToken.None);
        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
        var cashbackPersistido = (await new CashbackMySqlRepository(fixture.ConnectionFactory)
            .ObterPorIdAsync(pagamentoPix.CashbackId, CancellationToken.None))!;

        Assert.Equal(StatusAplicacaoPagamentoPix.Aplicado, aposReconciliacao.Status);
        Assert.Equal(StatusPagamentoPix.Concluido, pagamentoPersistido.Status);
        Assert.Equal(StatusCashback.Pago, cashbackPersistido.Status);
        Assert.Equal(1, provider.QuantidadeConsultas);
        Assert.Equal(0, provider.QuantidadeEnvios);
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoAplicacaoFinanceiraVencerCoordenacao_NaoDeveCriarConsultaNemChamarProvider()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync();
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var envio = OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1);
        await AdicionarEFinalizarAsync(operacaoRepository, envio, ResultadoOperacaoPagamentoPix.Confirmado);
        var aplicacao = new PagamentoPixAplicacaoResultadoService(
            CriarPagamentoRepository(),
            new CashbackMySqlRepository(fixture.ConnectionFactory),
            new PagamentoPixAplicacaoResultadoMySqlStore(fixture.ConnectionFactory));
        var provider = new PixProviderFake(PixProviderResult.Pendente());

        var aplicacaoResultado = await aplicacao.AplicarAsync(pagamentoPix.Id, CancellationToken.None);
        var reconciliacaoResultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);
        var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);

        Assert.Equal(StatusAplicacaoPagamentoPix.Aplicado, aplicacaoResultado.Status);
        Assert.Equal(StatusReconciliacaoPagamentoPix.NaoAplicavel, reconciliacaoResultado.Status);
        Assert.Single(operacoes);
        Assert.Equal(0, provider.QuantidadeConsultas);
        Assert.Equal(0, provider.QuantidadeEnvios);
    }

    private PagamentoPixReconciliacaoService CriarService(IPixProvider provider) =>
        new(
            CriarPagamentoRepository(),
            new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory),
            new PagamentoPixReconciliacaoMySqlStore(fixture.ConnectionFactory),
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

    private static OperacaoPagamentoPix CriarOperacaoAberta(
        Guid pagamentoPixId,
        TipoOperacaoPagamentoPix tipoOperacao,
        int? numeroTentativaEnvio,
        DateTime createdAt) =>
        OperacaoPagamentoPix.Reidratar(
            Guid.NewGuid(),
            pagamentoPixId,
            tipoOperacao,
            numeroTentativaEnvio,
            pagamentoPixId.ToString("N"),
            null,
            null,
            null,
            createdAt,
            createdAt,
            null);

    private static async Task AdicionarEFinalizarAsync(
        IOperacaoPagamentoPixRepository operacaoRepository,
        OperacaoPagamentoPix operacao,
        ResultadoOperacaoPagamentoPix resultado)
    {
        await operacaoRepository.AdicionarAsync(operacao, CancellationToken.None);
        operacao.Finalizar(resultado, "provider-id", "provider-code");
        Assert.True(await operacaoRepository.FinalizarAsync(operacao, CancellationToken.None));
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

    private async Task<string> ObterSnapshotCashbackAsync(Guid cashbackId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT CONCAT(status, ':', DATE_FORMAT(updated_at, '%Y-%m-%dT%H:%i:%s.%f')) FROM cashbacks WHERE id = @id;",
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = cashbackId.ToString();
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

    private sealed class PixProviderBloqueavel(PixProviderResult result) : IPixProvider
    {
        private readonly TaskCompletionSource _consultaIniciada = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _liberacao = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _quantidadeConsultas;
        private int _quantidadeEnvios;

        public Task ConsultaIniciada => _consultaIniciada.Task;
        public int QuantidadeConsultas => _quantidadeConsultas;
        public int QuantidadeEnvios => _quantidadeEnvios;

        public Task<PixProviderResult> EnviarAsync(PixEnvioRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _quantidadeEnvios);
            throw new InvalidOperationException("O envio não é permitido durante a reconciliação.");
        }

        public async Task<PixProviderResult> ConsultarAsync(PixConsultaRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _quantidadeConsultas);
            _consultaIniciada.TrySetResult();
            await _liberacao.Task.WaitAsync(cancellationToken);
            return result;
        }

        public void LiberarConsulta() => _liberacao.TrySetResult();
    }
}

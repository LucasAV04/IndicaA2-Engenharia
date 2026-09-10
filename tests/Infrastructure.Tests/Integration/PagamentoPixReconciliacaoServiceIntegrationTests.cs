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
[Trait("Category", MySqlIntegrationCategory.Name)]
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
        await DefinirLeaseAsync(pagamentoPix.Id, Guid.NewGuid(), expirado: false);
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
    public async Task ReconciliarAsync_QuandoLeaseDeEnvioForValido_DeveRetornarEnvioEmAndamentoSemConsulta()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync();
        var operacoes = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await operacoes.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1), CancellationToken.None);
        await DefinirLeaseEnvioAsync(pagamentoPix.Id, Guid.NewGuid(), expirado: false);
        var provider = new PixProviderFake(PixProviderResult.Confirmado());

        var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

        Assert.Equal(StatusReconciliacaoPagamentoPix.EnvioEmAndamento, resultado.Status);
        Assert.Equal(0, provider.QuantidadeConsultas);
        Assert.Single(await operacoes.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task ReconciliarAsync_QuandoLeaseDeEnvioExpirar_DeveIndicarRecuperacaoSemConsulta()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixProcessandoAsync();
        var operacoes = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await operacoes.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1), CancellationToken.None);
        await DefinirLeaseEnvioAsync(pagamentoPix.Id, Guid.NewGuid(), expirado: true);
        var provider = new PixProviderFake(PixProviderResult.Confirmado());

        var resultado = await CriarService(provider).ReconciliarAsync(pagamentoPix.Id, CancellationToken.None);

        Assert.Equal(StatusReconciliacaoPagamentoPix.EnvioPendenteRecuperacao, resultado.Status);
        Assert.Equal(0, provider.QuantidadeConsultas);
        Assert.Single(await operacoes.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None));
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
            Assert.NotNull(envioPersistido.FinishedAt);
            Assert.Equal(envioPersistido.FinishedAt.Value, envioPersistido.UpdatedAt);
            Assert.True(envioPersistido.FinishedAt.Value >= envioPersistido.CreatedAt);
            Assert.Equal(envioAtual.ReferenciaIdempotente, envioPersistido.ReferenciaIdempotente);
            Assert.Equal(envioAtual.NumeroTentativaEnvio, envioPersistido.NumeroTentativaEnvio);
            Assert.Equal(envioAtual.CreatedAt.Ticks / 10, envioPersistido.CreatedAt.Ticks / 10);
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
            new PagamentoPixAplicacaoResultadoMySqlStore(fixture.ConnectionFactory, new AesGcmDadosPixProtector(CriarChave())));

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
            new PagamentoPixAplicacaoResultadoMySqlStore(fixture.ConnectionFactory, new AesGcmDadosPixProtector(CriarChave())));
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

    [MySqlIntegrationFact]
    public async Task LeaseExpirado_DeveReutilizarConsultaERejeitarExecutorAntigo()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync();
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await repository.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1));
        var store = new PagamentoPixReconciliacaoMySqlStore(fixture.ConnectionFactory);
        var primeiro = await store.PrepararConsultaAsync(pagamento.Id);
        var consultaAntes = (await repository.ObterPorIdAsync(primeiro.OperacaoConsultaId!.Value))!;
        var materialAntes = await ObterMaterialProtegidoAsync(pagamento.Id);
        await DefinirLeaseAsync(pagamento.Id, primeiro.LeaseId!.Value, expirado: true);
        var expirado = await store.FinalizarConsultaAsync(pagamento.Id, consultaAntes.Id, primeiro.LeaseId.Value,
            ResultadoOperacaoPagamentoPix.Confirmado, "antigo", "antigo");
        Assert.False(expirado.Finalizada);
        var segundo = await store.PrepararConsultaAsync(pagamento.Id);
        Assert.Equal(primeiro.OperacaoConsultaId, segundo.OperacaoConsultaId);
        Assert.NotEqual(primeiro.LeaseId, segundo.LeaseId);
        var antigo = await store.FinalizarConsultaAsync(pagamento.Id, consultaAntes.Id, primeiro.LeaseId.Value,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada, "antigo", "antigo");
        Assert.False(antigo.Finalizada);
        var finalizacao = await store.FinalizarConsultaAsync(pagamento.Id, consultaAntes.Id, segundo.LeaseId!.Value,
            ResultadoOperacaoPagamentoPix.Confirmado, "novo", "confirmado");
        Assert.True(finalizacao.Finalizada);
        Assert.True(finalizacao.OperacaoEnvioAbertaResolvida);
        Assert.False((await store.FinalizarConsultaAsync(pagamento.Id, consultaAntes.Id, segundo.LeaseId.Value,
            ResultadoOperacaoPagamentoPix.Confirmado, "sobrescrita", "sobrescrita")).Finalizada);
        var historico = await repository.ObterPorPagamentoPixIdAsync(pagamento.Id);
        var consulta = Assert.Single(historico, x => x.TipoOperacao == TipoOperacaoPagamentoPix.Consulta);
        Assert.Equal(consultaAntes.CreatedAt, consulta.CreatedAt);
        Assert.Equal(consultaAntes.ReferenciaIdempotente, consulta.ReferenciaIdempotente);
        Assert.Equal("novo", consulta.IdentificadorProvider);
        Assert.All(historico, x => Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, x.Resultado));
        Assert.Equal(materialAntes, await ObterMaterialProtegidoAsync(pagamento.Id));
        await VerificarLeaseLivreAsync(pagamento.Id);
        await VerificarNaoLiquidadoAsync(pagamento);
    }

    [MySqlIntegrationFact]
    public async Task DuasReconciliacoes_DeveManterUmaConsultaVivaEAuditoriaAntesDoProvider()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync();
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await repository.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1));
        var provider = new PixProviderBloqueavel(PixProviderResult.Confirmado());
        var primeira = CriarService(provider).ReconciliarAsync(pagamento.Id);
        await provider.ConsultaIniciada.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            var antes = await repository.ObterPorPagamentoPixIdAsync(pagamento.Id);
            var aberta = Assert.Single(antes, x => x.TipoOperacao == TipoOperacaoPagamentoPix.Consulta);
            Assert.Null(aberta.FinishedAt);
            var concorrente = new PixProviderFake(PixProviderResult.FalhaConfirmada());
            var segunda = await CriarService(concorrente).ReconciliarAsync(pagamento.Id);
            Assert.Equal(StatusReconciliacaoPagamentoPix.ConsultaEmAndamento, segunda.Status);
            Assert.Equal(0, concorrente.QuantidadeConsultas);
            var aplicacao = await new PagamentoPixAplicacaoResultadoMySqlStore(
                fixture.ConnectionFactory, new AesGcmDadosPixProtector(CriarChave())).AplicarAsync(pagamento.Id);
            Assert.Equal(StatusPersistenciaAplicacaoPagamentoPix.RequerReconciliacao, aplicacao.Status);
            Assert.Null((await repository.ObterPorIdAsync(aberta.Id))!.FinishedAt);
            await VerificarNaoLiquidadoAsync(pagamento);
        }
        finally { provider.LiberarConsulta(); }
        await primeira;
        Assert.Equal(1, provider.QuantidadeConsultas);
        Assert.Single(await repository.ObterPorPagamentoPixIdAsync(pagamento.Id),
            x => x.TipoOperacao == TipoOperacaoPagamentoPix.Consulta);
    }

    [MySqlIntegrationFact]
    public async Task FalhaOuCancelamentoDoProvider_DeveAuditarIndeterminadoEPermitirConsultaPosterior()
    {
        foreach (var cancelar in new[] { false, true })
        {
            await fixture.LimparDadosAsync();
            var pagamento = await CriarPagamentoPixProcessandoAsync();
            var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
            await repository.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1));
            Exception falha = cancelar ? new OperationCanceledException() : new HttpRequestException("falha simulada");
            var provider = new PixProviderComFalha(falha);
            await Assert.ThrowsAnyAsync<Exception>(() => CriarService(provider).ReconciliarAsync(pagamento.Id));
            var consulta = Assert.Single(await repository.ObterPorPagamentoPixIdAsync(pagamento.Id),
                x => x.TipoOperacao == TipoOperacaoPagamentoPix.Consulta);
            Assert.Equal(ResultadoOperacaoPagamentoPix.Indeterminado, consulta.Resultado);
            Assert.NotNull(consulta.FinishedAt);
            await VerificarLeaseLivreAsync(pagamento.Id);
            await VerificarNaoLiquidadoAsync(pagamento);
            var posterior = await CriarService(new PixProviderFake(PixProviderResult.Pendente())).ReconciliarAsync(pagamento.Id);
            Assert.Equal(StatusReconciliacaoPagamentoPix.Consultado, posterior.Status);
            Assert.Equal(2, (await repository.ObterPorPagamentoPixIdAsync(pagamento.Id))
                .Count(x => x.TipoOperacao == TipoOperacaoPagamentoPix.Consulta));
        }
    }

    [MySqlIntegrationFact]
    public async Task FalhaNaFinalizacao_DeveReverterAuditoriaERecuperarMesmaConsultaAposExpiracao()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync();
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await repository.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1));
        await ExecutarSqlAsync("""
            CREATE TRIGGER tr_falha_finalizacao_consulta BEFORE UPDATE ON operacoes_pagamento_pix
            FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'falha simulada de auditoria';
            """);
        try
        {
            await Assert.ThrowsAsync<MySqlException>(() =>
                CriarService(new PixProviderFake(PixProviderResult.Confirmado())).ReconciliarAsync(pagamento.Id));
        }
        finally { await ExecutarSqlAsync("DROP TRIGGER tr_falha_finalizacao_consulta;"); }
        var consulta = Assert.Single(await repository.ObterPorPagamentoPixIdAsync(pagamento.Id),
            x => x.TipoOperacao == TipoOperacaoPagamentoPix.Consulta);
        Assert.Null(consulta.FinishedAt);
        await VerificarNaoLiquidadoAsync(pagamento);
        await ExecutarSqlAsync("UPDATE pagamentos_pix SET reconciliacao_lease_expira_em = UTC_TIMESTAMP(6) - INTERVAL 1 SECOND WHERE id = @id;", pagamento.Id);
        await CriarService(new PixProviderFake(PixProviderResult.Confirmado())).ReconciliarAsync(pagamento.Id);
        Assert.Equal(consulta.Id, Assert.Single(await repository.ObterPorPagamentoPixIdAsync(pagamento.Id),
            x => x.TipoOperacao == TipoOperacaoPagamentoPix.Consulta).Id);
        await VerificarLeaseLivreAsync(pagamento.Id);
    }

    [MySqlIntegrationFact]
    public async Task FinalizacaoConflitante_DeveFalharFechadoSemLiquidacao()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync();
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var envio = OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1);
        await repository.AdicionarAsync(envio);
        var store = new PagamentoPixReconciliacaoMySqlStore(fixture.ConnectionFactory);
        var preparacao = await store.PrepararConsultaAsync(pagamento.Id);
        envio.Finalizar(ResultadoOperacaoPagamentoPix.Confirmado);
        Assert.True(await repository.FinalizarAsync(envio));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.FinalizarConsultaAsync(pagamento.Id, preparacao.OperacaoConsultaId!.Value, preparacao.LeaseId!.Value,
                ResultadoOperacaoPagamentoPix.FalhaConfirmada, null, null));
        Assert.Null((await repository.ObterPorIdAsync(preparacao.OperacaoConsultaId!.Value))!.FinishedAt);
        await VerificarNaoLiquidadoAsync(pagamento);
    }

    [MySqlIntegrationFact]
    public async Task HistoricoInconsistente_DeveRejeitarReconciliacaoEAplicacaoSemProvider()
    {
        foreach (var cenario in new[] { "ausente", "anterior-aberto", "conflito", "duas-consultas" })
        {
            await fixture.LimparDadosAsync();
            var pagamento = await CriarPagamentoPixProcessandoAsync(cenario == "anterior-aberto" ? 2 : 1);
            var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
            var inicio = DateTime.UtcNow.AddMinutes(-5);
            if (cenario != "ausente")
                await repository.AdicionarAsync(CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Envio,
                    pagamento.QuantidadeTentativas, inicio.AddMinutes(1)));
            if (cenario == "anterior-aberto")
                await repository.AdicionarAsync(CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1, inicio));
            if (cenario is "conflito" or "duas-consultas")
            {
                var primeira = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null, inicio.AddMinutes(2));
                var segunda = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null, inicio.AddMinutes(3));
                if (cenario == "conflito")
                {
                    await AdicionarEFinalizarAsync(repository, primeira, ResultadoOperacaoPagamentoPix.Confirmado);
                    await AdicionarEFinalizarAsync(repository, segunda, ResultadoOperacaoPagamentoPix.FalhaConfirmada);
                }
                else
                {
                    await repository.AdicionarAsync(primeira);
                    await repository.AdicionarAsync(segunda);
                    await DefinirLeaseAsync(pagamento.Id, Guid.NewGuid(), expirado: true);
                }
            }
            var provider = new PixProviderFake(PixProviderResult.Confirmado());
            await Assert.ThrowsAsync<InvalidOperationException>(() => CriarService(provider).ReconciliarAsync(pagamento.Id));
            Assert.Equal(0, provider.QuantidadeConsultas);
            var aplicacao = new PagamentoPixAplicacaoResultadoMySqlStore(
                fixture.ConnectionFactory, new AesGcmDadosPixProtector(CriarChave()));
            if (cenario == "duas-consultas")
                Assert.Equal(StatusPersistenciaAplicacaoPagamentoPix.RequerReconciliacao,
                    (await aplicacao.AplicarAsync(pagamento.Id)).Status);
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => aplicacao.AplicarAsync(pagamento.Id));
            await VerificarNaoLiquidadoAsync(pagamento);
        }
    }

    [MySqlIntegrationFact]
    public async Task EstadosNaoAplicaveis_DevePreservarHistoricoSemProvider()
    {
        foreach (var status in Enum.GetValues<StatusPagamentoPix>().Where(x => x != StatusPagamentoPix.Processando))
        {
            await fixture.LimparDadosAsync();
            var pagamento = await CriarPagamentoPixProcessandoAsync();
            var tentativas = status == StatusPagamentoPix.Pendente ? 0 : status == StatusPagamentoPix.FalhaDefinitiva ? 5 : 1;
            await ExecutarSqlAsync($"UPDATE pagamentos_pix SET status = {(int)status}, quantidade_tentativas = {tentativas} WHERE id = @id;", pagamento.Id);
            var provider = new PixProviderFake(PixProviderResult.Confirmado());
            Assert.Equal(StatusReconciliacaoPagamentoPix.NaoAplicavel,
                (await CriarService(provider).ReconciliarAsync(pagamento.Id)).Status);
            Assert.Equal(0, provider.QuantidadeConsultas);
            Assert.Empty(await new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory).ObterPorPagamentoPixIdAsync(pagamento.Id));
        }
    }

    [MySqlIntegrationFact]
    public async Task ConsultaAntigaAberta_DevePermanecerIntactaAoFinalizarCicloAtual()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync(2);
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var inicio = DateTime.UtcNow.AddMinutes(-5);
        var antigo = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1, inicio);
        var consultaAntiga = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null, inicio.AddMinutes(1));
        var atual = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 2, inicio.AddMinutes(2));
        await AdicionarEFinalizarAsync(repository, antigo, ResultadoOperacaoPagamentoPix.FalhaConfirmada);
        await repository.AdicionarAsync(consultaAntiga);
        await repository.AdicionarAsync(atual);
        await CriarService(new PixProviderFake(PixProviderResult.Confirmado())).ReconciliarAsync(pagamento.Id);
        Assert.Null((await repository.ObterPorIdAsync(consultaAntiga.Id))!.FinishedAt);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, (await repository.ObterPorIdAsync(atual.Id))!.Resultado);
        Assert.Equal(ResultadoOperacaoPagamentoPix.FalhaConfirmada, (await repository.ObterPorIdAsync(antigo.Id))!.Resultado);
    }

    [MySqlIntegrationFact]
    public async Task EvidenciaPersistidaComConsultaAbandonada_DeveRecuperarSemHttp()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync();
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var inicio = DateTime.UtcNow.AddMinutes(-4);
        var envio = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1, inicio);
        var evidencia = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null, inicio.AddMinutes(1));
        var aberta = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null, inicio.AddMinutes(2));
        await repository.AdicionarAsync(envio);
        await AdicionarEFinalizarAsync(repository, evidencia, ResultadoOperacaoPagamentoPix.Confirmado);
        await repository.AdicionarAsync(aberta);
        await DefinirLeaseAsync(pagamento.Id, Guid.NewGuid(), expirado: true);
        var provider = new PixProviderFake(PixProviderResult.Pendente());
        Assert.Equal(StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo,
            (await CriarService(provider).ReconciliarAsync(pagamento.Id)).Status);
        Assert.Equal(0, provider.QuantidadeConsultas);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Indeterminado, (await repository.ObterPorIdAsync(aberta.Id))!.Resultado);
        Assert.Equal("provider-id", (await repository.ObterPorIdAsync(envio.Id))!.IdentificadorProvider);
        Assert.Equal(3, (await repository.ObterPorPagamentoPixIdAsync(pagamento.Id)).Count);
        await VerificarLeaseLivreAsync(pagamento.Id);
    }

    [MySqlIntegrationFact]
    public async Task LeaseNovo_DeveDurarCincoMinutosNoMySqlEValidarOperacaoETerminal()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync();
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await repository.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1));
        var store = new PagamentoPixReconciliacaoMySqlStore(fixture.ConnectionFactory);
        var preparacao = await store.PrepararConsultaAsync(pagamento.Id);
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT TIMESTAMPDIFF(SECOND, UTC_TIMESTAMP(6), reconciliacao_lease_expira_em) FROM pagamentos_pix WHERE id = @id;", connection);
        command.Parameters.AddWithValue("@id", pagamento.Id.ToString());
        Assert.InRange(Convert.ToInt32(await command.ExecuteScalarAsync()), 280, 300);
        Assert.False((await store.FinalizarConsultaAsync(pagamento.Id, Guid.NewGuid(), preparacao.LeaseId!.Value,
            ResultadoOperacaoPagamentoPix.Confirmado, null, null)).Finalizada);
        await ExecutarSqlAsync($"UPDATE pagamentos_pix SET status = {(int)StatusPagamentoPix.Concluido} WHERE id = @id;", pagamento.Id);
        Assert.False((await store.FinalizarConsultaAsync(pagamento.Id, preparacao.OperacaoConsultaId!.Value,
            preparacao.LeaseId.Value, ResultadoOperacaoPagamentoPix.Confirmado, null, null)).Finalizada);
        Assert.Null((await repository.ObterPorIdAsync(preparacao.OperacaoConsultaId.Value))!.FinishedAt);
    }

    [MySqlIntegrationFact]
    public async Task UnicidadeEnvio_DeveImpedirCicloComDoisEnviosPersistidos()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync();
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await repository.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1));
        await Assert.ThrowsAsync<MySqlException>(() =>
            repository.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1)));
        Assert.Single(await repository.ObterPorPagamentoPixIdAsync(pagamento.Id));
        await VerificarNaoLiquidadoAsync(pagamento);
    }

    [MySqlIntegrationFact]
    public async Task LeaseQueExpiraDuranteFinalizacao_DeveReverterTodaAuditoria()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync();
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await repository.AdicionarAsync(OperacaoPagamentoPix.IniciarEnvio(pagamento.Id, 1));
        var store = new PagamentoPixReconciliacaoMySqlStore(fixture.ConnectionFactory);
        var preparacao = await store.PrepararConsultaAsync(pagamento.Id);
        await ExecutarSqlAsync("""
            CREATE TRIGGER tr_expirar_lease BEFORE UPDATE ON operacoes_pagamento_pix FOR EACH ROW
            UPDATE pagamentos_pix SET reconciliacao_lease_expira_em = UTC_TIMESTAMP(6) - INTERVAL 1 SECOND
            WHERE id = NEW.pagamento_pix_id;
            """);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.FinalizarConsultaAsync(pagamento.Id, preparacao.OperacaoConsultaId!.Value, preparacao.LeaseId!.Value,
                    ResultadoOperacaoPagamentoPix.Confirmado, "provider", "code"));
        }
        finally { await ExecutarSqlAsync("DROP TRIGGER tr_expirar_lease;"); }
        Assert.All(await repository.ObterPorPagamentoPixIdAsync(pagamento.Id), x => Assert.Null(x.FinishedAt));
        Assert.Equal(StatusPreparacaoReconciliacaoPagamentoPix.ConsultaEmAndamento,
            (await store.PrepararConsultaAsync(pagamento.Id)).Status);
        await VerificarNaoLiquidadoAsync(pagamento);
    }

    [MySqlIntegrationFact]
    public async Task RecuperacaoEnvio_NaoDeveApagarMetadadosValidosComEvidenciaSemMetadados()
    {
        await fixture.LimparDadosAsync();
        var pagamento = await CriarPagamentoPixProcessandoAsync();
        var repository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var inicio = DateTime.UtcNow.AddMinutes(-2);
        var envio = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Envio, 1, inicio);
        var consulta = CriarOperacaoAberta(pagamento.Id, TipoOperacaoPagamentoPix.Consulta, null, inicio.AddMinutes(1));
        await repository.AdicionarAsync(envio);
        await repository.AdicionarAsync(consulta);
        consulta.Finalizar(ResultadoOperacaoPagamentoPix.Confirmado, " ", null);
        await repository.FinalizarAsync(consulta);
        // Dado legado fora das invariantes atuais: a correção não deve apagar metadados existentes.
        await ExecutarSqlAsync("""
            UPDATE operacoes_pagamento_pix SET identificador_provider = 'preservado', codigo = 'preservado'
            WHERE id = @id;
            """, envio.Id);
        await CriarService(new PixProviderFake(PixProviderResult.Pendente())).ReconciliarAsync(pagamento.Id);
        var persistido = (await repository.ObterPorIdAsync(envio.Id))!;
        Assert.Equal("preservado", persistido.IdentificadorProvider);
        Assert.Equal("preservado", persistido.Codigo);
    }

    private async Task DefinirLeaseAsync(Guid pagamentoId, Guid leaseId, bool expirado)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("""
            UPDATE pagamentos_pix SET reconciliacao_lease_id = @leaseId,
            reconciliacao_lease_expira_em = TIMESTAMPADD(SECOND, @segundos, UTC_TIMESTAMP(6))
            WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("@id", pagamentoId.ToString());
        command.Parameters.AddWithValue("@leaseId", leaseId.ToString());
        command.Parameters.AddWithValue("@segundos", expirado ? -1 : 300);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DefinirLeaseEnvioAsync(Guid pagamentoId, Guid leaseId, bool expirado)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("""
            UPDATE pagamentos_pix SET envio_lease_id = @leaseId,
            envio_lease_expira_em = TIMESTAMPADD(SECOND, @segundos, UTC_TIMESTAMP(6))
            WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("@id", pagamentoId.ToString());
        command.Parameters.AddWithValue("@leaseId", leaseId.ToString());
        command.Parameters.AddWithValue("@segundos", expirado ? -1 : 300);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecutarSqlAsync(string sql, Guid? id = null)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        if (id.HasValue) command.Parameters.AddWithValue("@id", id.Value.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private async Task VerificarLeaseLivreAsync(Guid id)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT reconciliacao_lease_id IS NULL AND reconciliacao_lease_expira_em IS NULL FROM pagamentos_pix WHERE id = @id;", connection);
        command.Parameters.AddWithValue("@id", id.ToString());
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    private async Task VerificarNaoLiquidadoAsync(PagamentoPix pagamento)
    {
        Assert.Equal(StatusPagamentoPix.Processando, (await CriarPagamentoRepository().ObterPorIdAsync(pagamento.Id))!.Status);
        Assert.Equal(StatusCashback.Disponivel,
            (await new CashbackMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(pagamento.CashbackId))!.Status);
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

    private sealed class PixProviderComFalha(Exception falha) : IPixProvider
    {
        public Task<PixProviderResult> ConsultarAsync(PixConsultaRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<PixProviderResult>(falha);
        public Task<PixProviderResult> EnviarAsync(PixEnvioRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Envio não permitido no teste.");
    }

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

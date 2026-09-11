using Application.Interfaces.Providers;
using Application.Models;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Infrastructure.Security;
using MySqlConnector;
using System.Collections.Concurrent;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", MySqlIntegrationCategory.Name)]
public sealed class PagamentoPixEnvioMySqlStoreIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task TentarPrepararEnvioAsync_QuandoPendente_DeveAlterarOrdemECriarAuditoriaNaMesmaTransacao()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var pagamentoRepository = CriarPagamentoRepository();
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var store = new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory);
        var materialAntes = await ObterMaterialProtegidoAsync(pagamentoPix.Id);

        var preparacao = await store.TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);

        var reidratado = (await pagamentoRepository.ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
        var operacao = (await operacaoRepository.ObterPorIdAsync(
            preparacao.OperacaoPagamentoPixId!.Value,
            CancellationToken.None))!;
        var lease = await ObterLeaseEnvioAsync(pagamentoPix.Id);
        var materialDepois = await ObterMaterialProtegidoAsync(pagamentoPix.Id);

        Assert.True(preparacao.Adquirido);
        Assert.Equal(1, preparacao.NumeroTentativaEnvio);
        Assert.Equal(StatusPagamentoPix.Processando, reidratado.Status);
        Assert.Equal(1, reidratado.QuantidadeTentativas);
        Assert.Equal(pagamentoPix.Id, operacao.PagamentoPixId);
        Assert.Equal(TipoOperacaoPagamentoPix.Envio, operacao.TipoOperacao);
        Assert.Equal(1, operacao.NumeroTentativaEnvio);
        Assert.Equal(pagamentoPix.Id.ToString("N"), operacao.ReferenciaIdempotente);
        Assert.False(operacao.FinishedAt.HasValue);
        Assert.NotNull(preparacao.LeaseId);
        Assert.Equal(preparacao.LeaseId, lease.LeaseId);
        Assert.True(lease.ExpiraEm > lease.Agora);
        Assert.Equal(materialAntes, materialDepois);
    }

    [MySqlIntegrationFact]
    public async Task TentarPrepararEnvioAsync_QuandoAuditoriaDuplicadaFalhar_DeveReverterClaim()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var pagamentoRepository = CriarPagamentoRepository();
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await operacaoRepository.AdicionarAsync(
            OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, 1),
            CancellationToken.None);

        await Assert.ThrowsAsync<MySqlException>(() =>
            new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory)
                .TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None));

        var reidratado = (await pagamentoRepository.ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
        var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);

        Assert.Equal(StatusPagamentoPix.Pendente, reidratado.Status);
        Assert.Equal(0, reidratado.QuantidadeTentativas);
        Assert.Single(operacoes);
    }

    [MySqlIntegrationFact]
    public async Task TentarPrepararEnvioAsync_QuandoQuartaFalhaExistir_DevePermitirQuintaTentativa()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync(StatusPagamentoPix.Falhou, 4);

        var preparacao = await new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory)
            .TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);

        var reidratado = (await CriarPagamentoRepository()
            .ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
        Assert.True(preparacao.Adquirido);
        Assert.Equal(PagamentoPix.TentativasMaximas, preparacao.NumeroTentativaEnvio);
        Assert.Equal(StatusPagamentoPix.Processando, reidratado.Status);
        Assert.Equal(PagamentoPix.TentativasMaximas, reidratado.QuantidadeTentativas);
    }

    [MySqlIntegrationFact]
    public async Task TentarPrepararEnvioAsync_QuandoEstadoNaoForElegivel_DeveRecusarSemCriarAuditoria()
    {
        var cenarios = new[]
        {
            (StatusPagamentoPix.Processando, 1),
            (StatusPagamentoPix.Concluido, 1),
            (StatusPagamentoPix.FalhaDefinitiva, PagamentoPix.TentativasMaximas),
            (StatusPagamentoPix.Cancelado, 0)
        };

        foreach (var (status, tentativas) in cenarios)
        {
            await fixture.LimparDadosAsync();
            var pagamentoPix = await CriarPagamentoPixPersistidoAsync(status, tentativas);
            var preparacao = await new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory)
                .TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);
            var operacoes = await new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory)
                .ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);

            Assert.False(preparacao.Adquirido);
            Assert.Empty(operacoes);
        }
    }

    [MySqlIntegrationFact]
    public async Task ProcessarEnvioAsync_QuandoCincoExecutoresConcorrerem_DeveChamarProviderUmaUnicaVez()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var provider = new PixProviderFake();
        var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tarefas = Enumerable.Range(0, 5)
            .Select(async _ =>
            {
                await inicio.Task;
                return await CriarOrquestrador(provider).ProcessarEnvioAsync(
                    pagamentoPix.Id,
                    CancellationToken.None);
            })
            .ToArray();

        inicio.SetResult();
        var resultados = await Task.WhenAll(tarefas);

        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
        var operacoes = await new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory)
            .ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);

        Assert.Equal(1, resultados.Count(resultado => resultado.EnvioExecutado));
        Assert.Equal(1, provider.QuantidadeEnvios);
        Assert.Equal(StatusPagamentoPix.Processando, pagamentoPersistido.Status);
        Assert.Equal(1, pagamentoPersistido.QuantidadeTentativas);
        Assert.Single(operacoes);
        Assert.True(operacoes.Single().FinishedAt.HasValue);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, operacoes.Single().Resultado);
        var lease = await ObterLeaseEnvioAsync(pagamentoPix.Id);
        Assert.Null(lease.LeaseId);
        Assert.Null(lease.ExpiraEm);
    }

    private PagamentoPixEnvioService CriarOrquestrador(IPixProvider provider) =>
        new(
            CriarPagamentoRepository(),
            new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory),
            provider);

    private async Task<PagamentoPix> CriarPagamentoPixPersistidoAsync(
        StatusPagamentoPix status = StatusPagamentoPix.Pendente,
        int quantidadeTentativas = 0)
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
        var indicacao = new Indicacao(indicador.Id, "Indicada Envio", "11999999999", indicador.CodigoIndicacao!);
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
        var paraPersistir = status == StatusPagamentoPix.Pendente && quantidadeTentativas == 0
            ? pagamentoPix
            : PagamentoPix.Reidratar(
                pagamentoPix.Id,
                pagamentoPix.CashbackId,
                pagamentoPix.UsuarioBeneficiarioId,
                pagamentoPix.Valor,
                pagamentoPix.TipoChavePix,
                pagamentoPix.ChavePix,
                status,
                quantidadeTentativas,
                pagamentoPix.CreatedAt,
                pagamentoPix.UpdatedAt);
        await CriarPagamentoRepository().AdicionarAsync(paraPersistir, CancellationToken.None);
        return paraPersistir;
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

    [MySqlIntegrationFact]
    public async Task TentarPrepararEnvioAsync_QuandoLeaseExpirar_DeveRecuperarMesmaOperacaoSemNovaTentativa()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var store = new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory);
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);

        var primeira = await store.TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);
        await ExpirarLeaseEnvioAsync(pagamentoPix.Id, primeira.LeaseId!.Value);

        var recuperada = await store.TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);
        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
        var operacoes = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);

        Assert.True(recuperada.Adquirido);
        Assert.Equal(primeira.OperacaoPagamentoPixId, recuperada.OperacaoPagamentoPixId);
        Assert.Equal(primeira.NumeroTentativaEnvio, recuperada.NumeroTentativaEnvio);
        Assert.Equal(primeira.ReferenciaIdempotente, recuperada.ReferenciaIdempotente);
        Assert.NotEqual(primeira.LeaseId, recuperada.LeaseId);
        Assert.Equal(1, pagamentoPersistido.QuantidadeTentativas);
        Assert.Single(operacoes);
    }

    [MySqlIntegrationFact]
    public async Task ProcessarEnvioAsync_QuandoLeaseExpirar_DeveReutilizarIdEnvioERejeitarExecutorAntigo()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var provider = new PixProviderIdempotenteBloqueavelFake();
        var executorAntigo = CriarOrquestrador(provider).ProcessarEnvioAsync(pagamentoPix.Id, CancellationToken.None);
        Exception? falhaExecutorAntigo = null;
        ResultadoEnvioPagamentoPix? recuperada = null;
        OperacaoPagamentoPix? envioAntesDaRecuperacao = null;
        (Guid? LeaseId, DateTime? ExpiraEm, DateTime Agora) leaseA = default;
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);

        try
        {
            await provider.PrimeiroEnvioIniciado.WaitAsync(TimeSpan.FromSeconds(10));
            envioAntesDaRecuperacao = Assert.Single(await operacaoRepository.ObterPorPagamentoPixIdAsync(
                pagamentoPix.Id,
                CancellationToken.None));
            leaseA = await ObterLeaseEnvioAsync(pagamentoPix.Id);
            Assert.NotNull(leaseA.LeaseId);
            await ExpirarLeaseEnvioAsync(pagamentoPix.Id, leaseA.LeaseId!.Value);

            recuperada = await CriarOrquestrador(provider).ProcessarEnvioAsync(pagamentoPix.Id, CancellationToken.None);
        }
        finally
        {
            provider.LiberarPrimeiroEnvio();
            try
            {
                await executorAntigo.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception)
            {
                falhaExecutorAntigo = exception;
            }
        }

        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(pagamentoPix.Id, CancellationToken.None))!;
        var cashbackPersistido = (await new CashbackMySqlRepository(fixture.ConnectionFactory)
            .ObterPorIdAsync(pagamentoPix.CashbackId, CancellationToken.None))!;
        var envios = await operacaoRepository.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None);
        var leaseFinal = await ObterLeaseEnvioAsync(pagamentoPix.Id);

        Assert.True(recuperada!.EnvioExecutado);
        Assert.Equal(2, provider.QuantidadeEnvios);
        Assert.Equal(1, provider.QuantidadeEfeitosLogicos);
        Assert.Equal(envioAntesDaRecuperacao!.ReferenciaIdempotente, provider.Referencias.Single());
        Assert.Equal(envioAntesDaRecuperacao.Id, recuperada.OperacaoPagamentoPixId);
        var falhaEsperada = Assert.IsType<InvalidOperationException>(falhaExecutorAntigo);
        Assert.Contains("lease de envio não autorizou", falhaEsperada.Message);
        Assert.Equal(1, pagamentoPersistido.QuantidadeTentativas);
        Assert.Equal(StatusPagamentoPix.Processando, pagamentoPersistido.Status);
        Assert.Equal(StatusCashback.Disponivel, cashbackPersistido.Status);
        var envioFinal = Assert.Single(envios);
        Assert.Equal(1, envioFinal.NumeroTentativaEnvio);
        Assert.True(envioFinal.FinishedAt.HasValue);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, envioFinal.Resultado);
        Assert.Null(leaseFinal.LeaseId);
        Assert.Null(leaseFinal.ExpiraEm);
    }

    [MySqlIntegrationFact]
    public async Task TentarPrepararEnvioAsync_QuandoEnvioEConsultaEstiveremAbertos_DeveFalharSemMutacao()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var store = new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory);
        var primeira = await store.TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);
        var operacoes = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        await operacoes.AdicionarAsync(OperacaoPagamentoPix.IniciarConsulta(pagamentoPix.Id), CancellationToken.None);
        var leaseAntes = await ObterLeaseEnvioAsync(pagamentoPix.Id);

        var excecao = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None));

        Assert.Contains("Envio e Consulta abertos simultaneamente", excecao.Message);
        var leaseDepois = await ObterLeaseEnvioAsync(pagamentoPix.Id);
        Assert.Equal(leaseAntes.LeaseId, leaseDepois.LeaseId);
        Assert.Equal(leaseAntes.ExpiraEm, leaseDepois.ExpiraEm);
        Assert.Equal(2, (await operacoes.ObterPorPagamentoPixIdAsync(pagamentoPix.Id, CancellationToken.None)).Count);
    }

    [MySqlIntegrationFact]
    public async Task TentarPrepararEnvioAsync_QuandoLeaseParcialOuSimultaneoExistir_DeveFalharFechado()
    {
        foreach (var (envioId, envioExpira, reconciliacaoId, reconciliacaoExpira) in new[]
                 {
                     (Guid.NewGuid().ToString(), (object)DBNull.Value, (object)DBNull.Value, (object)DBNull.Value),
                     ((object)DBNull.Value, (object)DateTime.UtcNow.AddMinutes(5), (object)DBNull.Value, (object)DBNull.Value),
                     (Guid.NewGuid().ToString(), (object)DateTime.UtcNow.AddMinutes(5), Guid.NewGuid().ToString(), (object)DateTime.UtcNow.AddMinutes(5))
                 })
        {
            await fixture.LimparDadosAsync();
            var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
            await using var connection = fixture.ConnectionFactory.Create();
            await connection.OpenAsync();
            await using (var command = new MySqlCommand("""
                UPDATE pagamentos_pix
                SET envio_lease_id = @envioId,
                    envio_lease_expira_em = @envioExpira,
                    reconciliacao_lease_id = @reconciliacaoId,
                    reconciliacao_lease_expira_em = @reconciliacaoExpira
                WHERE id = @id;
                """, connection))
            {
                command.Parameters.AddWithValue("@id", pagamentoPix.Id.ToString());
                command.Parameters.AddWithValue("@envioId", envioId);
                command.Parameters.AddWithValue("@envioExpira", envioExpira);
                command.Parameters.AddWithValue("@reconciliacaoId", reconciliacaoId);
                command.Parameters.AddWithValue("@reconciliacaoExpira", reconciliacaoExpira);
                await command.ExecuteNonQueryAsync();
            }

            var pagamentoAntes = await ObterSnapshotPagamentoBrutoAsync(pagamentoPix.Id);
            var cashbackAntes = await ObterSnapshotCashbackBrutoAsync(pagamentoPix.CashbackId);
            var operacoesAntes = await ObterOperacoesBrutasAsync(pagamentoPix.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory)
                    .TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None));

            Assert.Equal(pagamentoAntes, await ObterSnapshotPagamentoBrutoAsync(pagamentoPix.Id));
            Assert.Equal(cashbackAntes, await ObterSnapshotCashbackBrutoAsync(pagamentoPix.CashbackId));
            Assert.Equal(operacoesAntes, await ObterOperacoesBrutasAsync(pagamentoPix.Id));
        }
    }

    [MySqlIntegrationFact]
    public async Task FinalizarEnvioAsync_QuandoTokenForIncorretoOuLeaseExpirar_DevePreservarAuditoriaAberta()
    {
        foreach (var expirarLease in new[] { false, true })
        {
            await fixture.LimparDadosAsync();
            var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
            var store = new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory);
            var preparacao = await store.TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);
            if (expirarLease)
                await ExpirarLeaseEnvioAsync(pagamentoPix.Id, preparacao.LeaseId!.Value);

            var resultado = await store.FinalizarEnvioAsync(
                pagamentoPix.Id,
                preparacao.OperacaoPagamentoPixId!.Value,
                expirarLease ? preparacao.LeaseId!.Value : Guid.NewGuid(),
                ResultadoOperacaoPagamentoPix.Confirmado,
                "provider-id",
                "provider-code",
                CancellationToken.None);

            var auditoria = (await new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory)
                .ObterPorIdAsync(preparacao.OperacaoPagamentoPixId.Value, CancellationToken.None))!;
            var lease = await ObterLeaseEnvioAsync(pagamentoPix.Id);
            Assert.False(resultado.Finalizada);
            Assert.Null(auditoria.FinishedAt);
            Assert.Null(auditoria.Resultado);
            Assert.Null(auditoria.IdentificadorProvider);
            Assert.Null(auditoria.Codigo);
            Assert.Equal(preparacao.LeaseId, lease.LeaseId);
        }
    }

    [MySqlIntegrationFact]
    public async Task FinalizarEnvioAsync_QuandoAuditoriaAbertaForAdulterada_DeveFalharFechadoEPreservarDados()
    {
        var adulteracoes = new[]
        {
            new AdulteracaoAuditoria("referencia_idempotente = 'referencia-adulterada'", DeveLancar: true),
            new AdulteracaoAuditoria($"resultado = {(int)ResultadoOperacaoPagamentoPix.Confirmado}", DeveLancar: true),
            new AdulteracaoAuditoria("identificador_provider = 'provider-adulterado'", DeveLancar: true),
            new AdulteracaoAuditoria("codigo = 'codigo-adulterado'", DeveLancar: true),
            new AdulteracaoAuditoria($"tipo_operacao = {(int)TipoOperacaoPagamentoPix.Consulta}", DeveLancar: false),
            new AdulteracaoAuditoria("numero_tentativa_envio = 99", DeveLancar: false)
        };

        foreach (var adulteracao in adulteracoes)
        {
            await fixture.LimparDadosAsync();
            var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
            var store = new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory);
            var preparacao = await store.TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);
            await using (var connection = fixture.ConnectionFactory.Create())
            {
                await connection.OpenAsync();
                await using var command = new MySqlCommand(
                    $"UPDATE operacoes_pagamento_pix SET {adulteracao.Sql} WHERE id = @id;", connection);
                command.Parameters.AddWithValue("@id", preparacao.OperacaoPagamentoPixId!.Value.ToString());
                await command.ExecuteNonQueryAsync();
            }

            var auditoriaAntes = await ObterAuditoriaBrutaAsync(preparacao.OperacaoPagamentoPixId!.Value);
            var leaseAntes = await ObterSnapshotPagamentoBrutoAsync(pagamentoPix.Id);
            var cashbackAntes = await ObterSnapshotCashbackBrutoAsync(pagamentoPix.CashbackId);
            if (adulteracao.DeveLancar)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.FinalizarEnvioAsync(
                    pagamentoPix.Id,
                    preparacao.OperacaoPagamentoPixId!.Value,
                    preparacao.LeaseId!.Value,
                    ResultadoOperacaoPagamentoPix.Confirmado,
                    "provider-id",
                    "provider-code",
                    CancellationToken.None));
            }
            else
            {
                var finalizacao = await store.FinalizarEnvioAsync(
                    pagamentoPix.Id,
                    preparacao.OperacaoPagamentoPixId!.Value,
                    preparacao.LeaseId!.Value,
                    ResultadoOperacaoPagamentoPix.Confirmado,
                    "provider-id",
                    "provider-code",
                    CancellationToken.None);
                Assert.False(finalizacao.Finalizada);
            }

            Assert.Equal(auditoriaAntes, await ObterAuditoriaBrutaAsync(preparacao.OperacaoPagamentoPixId.Value));
            Assert.Equal(leaseAntes, await ObterSnapshotPagamentoBrutoAsync(pagamentoPix.Id));
            Assert.Equal(cashbackAntes, await ObterSnapshotCashbackBrutoAsync(pagamentoPix.CashbackId));
        }
    }

    [MySqlIntegrationFact]
    public async Task FinalizarEnvioAsync_QuandoAuditoriaOuLiberacaoDoLeaseFalhar_DeveReverterIntegralmente()
    {
        foreach (var (nomeTrigger, sqlTrigger) in new[]
                 {
                     ("tr_falha_finalizar_envio_auditoria", "CREATE TRIGGER tr_falha_finalizar_envio_auditoria BEFORE UPDATE ON operacoes_pagamento_pix FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'falha de auditoria induzida';"),
                     ("tr_falha_liberar_envio_lease", "CREATE TRIGGER tr_falha_liberar_envio_lease BEFORE UPDATE ON pagamentos_pix FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'falha de lease induzida';")
                 })
        {
            await fixture.LimparDadosAsync();
            var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
            var store = new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory);
            var preparacao = await store.TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);
            await ExecutarSqlAsync(sqlTrigger);
            try
            {
                await Assert.ThrowsAsync<MySqlException>(() => store.FinalizarEnvioAsync(
                    pagamentoPix.Id,
                    preparacao.OperacaoPagamentoPixId!.Value,
                    preparacao.LeaseId!.Value,
                    ResultadoOperacaoPagamentoPix.Confirmado,
                    "provider-id",
                    "provider-code",
                    CancellationToken.None));
            }
            finally
            {
                await ExecutarSqlAsync($"DROP TRIGGER IF EXISTS {nomeTrigger};");
            }

            var auditoria = (await new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory)
                .ObterPorIdAsync(preparacao.OperacaoPagamentoPixId!.Value, CancellationToken.None))!;
            Assert.Null(auditoria.FinishedAt);
            Assert.Null(auditoria.Resultado);
            Assert.Equal(preparacao.LeaseId, (await ObterLeaseEnvioAsync(pagamentoPix.Id)).LeaseId);
        }
    }

    [MySqlIntegrationFact]
    public async Task FinalizarEnvioAsync_QuandoLeaseExpirarDuranteFinalizacao_DeveReverterIntegralmente()
    {
        await fixture.LimparDadosAsync();
        var pagamentoPix = await CriarPagamentoPixPersistidoAsync();
        var store = new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory);
        var preparacao = await store.TentarPrepararEnvioAsync(pagamentoPix.Id, CancellationToken.None);
        var pagamentoAntes = await ObterSnapshotPagamentoBrutoAsync(pagamentoPix.Id);
        var auditoriaAntes = await ObterAuditoriaBrutaAsync(preparacao.OperacaoPagamentoPixId!.Value);
        var cashbackAntes = await ObterSnapshotCashbackBrutoAsync(pagamentoPix.CashbackId);
        await ExecutarSqlAsync("""
            CREATE TRIGGER tr_expirar_lease_envio_durante_finalizacao
            BEFORE UPDATE ON operacoes_pagamento_pix FOR EACH ROW
            UPDATE pagamentos_pix
            SET envio_lease_expira_em = UTC_TIMESTAMP(6) - INTERVAL 1 SECOND
            WHERE id = NEW.pagamento_pix_id;
            """);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.FinalizarEnvioAsync(
                pagamentoPix.Id,
                preparacao.OperacaoPagamentoPixId!.Value,
                preparacao.LeaseId!.Value,
                ResultadoOperacaoPagamentoPix.Confirmado,
                "provider-id",
                "provider-code",
                CancellationToken.None));
        }
        finally
        {
            await ExecutarSqlAsync("DROP TRIGGER IF EXISTS tr_expirar_lease_envio_durante_finalizacao;");
        }

        Assert.Equal(auditoriaAntes, await ObterAuditoriaBrutaAsync(preparacao.OperacaoPagamentoPixId!.Value));
        Assert.Equal(pagamentoAntes, await ObterSnapshotPagamentoBrutoAsync(pagamentoPix.Id));
        Assert.Equal(cashbackAntes, await ObterSnapshotCashbackBrutoAsync(pagamentoPix.CashbackId));
    }

    private async Task<(Guid? LeaseId, DateTime? ExpiraEm, DateTime Agora)> ObterLeaseEnvioAsync(Guid pagamentoPixId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT envio_lease_id, envio_lease_expira_em, UTC_TIMESTAMP(6) FROM pagamentos_pix WHERE id = @id;",
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = pagamentoPixId.ToString();
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Guid? id = reader.IsDBNull(0) ? null : reader.ObterGuid("envio_lease_id");
        DateTime? expira = reader.IsDBNull(1) ? null : DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
        return (id, expira, DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc));
    }

    private async Task ExpirarLeaseEnvioAsync(Guid pagamentoPixId, Guid leaseId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "UPDATE pagamentos_pix SET envio_lease_expira_em = UTC_TIMESTAMP(6) - INTERVAL 1 SECOND WHERE id = @id AND envio_lease_id = @leaseId;",
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = pagamentoPixId.ToString();
        command.Parameters.Add("@leaseId", MySqlDbType.VarChar).Value = leaseId.ToString();
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task ExecutarSqlAsync(string sql)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SnapshotPagamentoBruto> ObterSnapshotPagamentoBrutoAsync(Guid pagamentoPixId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("""
            SELECT status, quantidade_tentativas, envio_lease_id, envio_lease_expira_em,
                   reconciliacao_lease_id, reconciliacao_lease_expira_em, updated_at
            FROM pagamentos_pix WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("@id", pagamentoPixId.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new SnapshotPagamentoBruto(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.ObterGuid("envio_lease_id"),
            reader.IsDBNull(3) ? null : DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
            reader.IsDBNull(4) ? null : reader.ObterGuid("reconciliacao_lease_id"),
            reader.IsDBNull(5) ? null : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc));
    }

    private async Task<SnapshotCashbackBruto> ObterSnapshotCashbackBrutoAsync(Guid cashbackId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT status, updated_at FROM cashbacks WHERE id = @id;", connection);
        command.Parameters.AddWithValue("@id", cashbackId.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new SnapshotCashbackBruto(reader.GetInt32(0), DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
    }

    private async Task<IReadOnlyList<SnapshotAuditoriaBruta>> ObterOperacoesBrutasAsync(Guid pagamentoPixId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT id, pagamento_pix_id, tipo_operacao, numero_tentativa_envio, referencia_idempotente, resultado, identificador_provider, codigo, started_at, updated_at, finished_at FROM operacoes_pagamento_pix WHERE pagamento_pix_id = @id ORDER BY started_at, id;", connection);
        command.Parameters.AddWithValue("@id", pagamentoPixId.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        var snapshots = new List<SnapshotAuditoriaBruta>();
        while (await reader.ReadAsync())
            snapshots.Add(CriarSnapshotAuditoriaBruta(reader));
        return snapshots;
    }

    private async Task<SnapshotAuditoriaBruta> ObterAuditoriaBrutaAsync(Guid operacaoId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT id, pagamento_pix_id, tipo_operacao, numero_tentativa_envio, referencia_idempotente, resultado, identificador_provider, codigo, started_at, updated_at, finished_at FROM operacoes_pagamento_pix WHERE id = @id;", connection);
        command.Parameters.AddWithValue("@id", operacaoId.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return CriarSnapshotAuditoriaBruta(reader);
    }

    private static SnapshotAuditoriaBruta CriarSnapshotAuditoriaBruta(MySqlDataReader reader) => new(
        reader.ObterGuid("id"),
        reader.ObterGuid("pagamento_pix_id"),
        reader.GetInt32(reader.GetOrdinal("tipo_operacao")),
        reader.IsDBNull(reader.GetOrdinal("numero_tentativa_envio")) ? null : reader.GetInt32(reader.GetOrdinal("numero_tentativa_envio")),
        reader.GetString(reader.GetOrdinal("referencia_idempotente")),
        reader.IsDBNull(reader.GetOrdinal("resultado")) ? null : reader.GetInt32(reader.GetOrdinal("resultado")),
        reader.IsDBNull(reader.GetOrdinal("identificador_provider")) ? null : reader.GetString(reader.GetOrdinal("identificador_provider")),
        reader.IsDBNull(reader.GetOrdinal("codigo")) ? null : reader.GetString(reader.GetOrdinal("codigo")),
        DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("started_at")), DateTimeKind.Utc),
        DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("updated_at")), DateTimeKind.Utc),
        reader.IsDBNull(reader.GetOrdinal("finished_at")) ? null : DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("finished_at")), DateTimeKind.Utc));

    private static string CriarChave() =>
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(valor => (byte)valor).ToArray());

    private sealed record AdulteracaoAuditoria(string Sql, bool DeveLancar);
    private sealed record SnapshotPagamentoBruto(int Status, int QuantidadeTentativas, Guid? EnvioLeaseId,
        DateTime? EnvioLeaseExpiraEm, Guid? ReconciliacaoLeaseId, DateTime? ReconciliacaoLeaseExpiraEm, DateTime UpdatedAt);
    private sealed record SnapshotCashbackBruto(int Status, DateTime UpdatedAt);
    private sealed record SnapshotAuditoriaBruta(Guid Id, Guid PagamentoPixId, int TipoOperacao,
        int? NumeroTentativaEnvio, string ReferenciaIdempotente, int? Resultado, string? IdentificadorProvider,
        string? Codigo, DateTime StartedAt, DateTime UpdatedAt, DateTime? FinishedAt);

    private sealed class PixProviderFake : IPixProvider
    {
        private int _quantidadeEnvios;

        public int QuantidadeEnvios => _quantidadeEnvios;

        public Task<PixProviderResult> EnviarAsync(
            PixEnvioRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _quantidadeEnvios);
            return Task.FromResult(PixProviderResult.Confirmado("provider-id", "provider-code"));
        }

        public Task<PixProviderResult> ConsultarAsync(
            PixConsultaRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PixProviderIdempotenteBloqueavelFake : IPixProvider
    {
        private readonly ConcurrentDictionary<string, byte> _referencias = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _primeiroEnvioIniciado = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _liberarPrimeiroEnvio = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _quantidadeEnvios;

        public int QuantidadeEnvios => _quantidadeEnvios;
        public int QuantidadeEfeitosLogicos => _referencias.Count;
        public IReadOnlyCollection<string> Referencias => _referencias.Keys.ToArray();
        public Task PrimeiroEnvioIniciado => _primeiroEnvioIniciado.Task;

        public async Task<PixProviderResult> EnviarAsync(PixEnvioRequest request, CancellationToken cancellationToken = default)
        {
            var chamada = Interlocked.Increment(ref _quantidadeEnvios);
            _referencias.TryAdd(request.ReferenciaIdempotente, 0);
            if (chamada == 1)
            {
                _primeiroEnvioIniciado.TrySetResult();
                await _liberarPrimeiroEnvio.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return PixProviderResult.Confirmado();
        }

        public Task<PixProviderResult> ConsultarAsync(PixConsultaRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void LiberarPrimeiroEnvio() => _liberarPrimeiroEnvio.TrySetResult();
    }
}

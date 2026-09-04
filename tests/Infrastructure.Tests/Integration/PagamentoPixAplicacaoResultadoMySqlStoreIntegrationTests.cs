using Application.Interfaces.Stores;
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
public sealed class PagamentoPixAplicacaoResultadoMySqlStoreIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task AplicarAsync_QuandoConfirmado_DeveConcluirPagamentoEPagarCashbackAtomicamente()
    {
        await fixture.LimparDadosAsync();
        var contexto = await CriarContextoPersistidoAsync(ResultadoOperacaoPagamentoPix.Confirmado);
        var materialAntes = await ObterMaterialProtegidoAsync(contexto.PagamentoPix.Id);
        var auditoriaAntes = await ObterSnapshotAuditoriaAsync(contexto.PagamentoPix.Id);
        var cashbackAntes = await ObterSnapshotCashbackAsync(contexto.Cashback.Id);

        var resultado = await CriarService().AplicarAsync(contexto.PagamentoPix.Id, CancellationToken.None);

        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(contexto.PagamentoPix.Id, CancellationToken.None))!;
        var cashbackPersistido = (await CriarCashbackRepository()
            .ObterPorIdAsync(contexto.Cashback.Id, CancellationToken.None))!;

        Assert.Equal(StatusAplicacaoPagamentoPix.Aplicado, resultado.Status);
        Assert.Equal(StatusPagamentoPix.Concluido, pagamentoPersistido.Status);
        Assert.Equal(StatusCashback.Pago, cashbackPersistido.Status);
        Assert.Equal(contexto.PagamentoPix.QuantidadeTentativas, pagamentoPersistido.QuantidadeTentativas);
        Assert.Equal(materialAntes, await ObterMaterialProtegidoAsync(contexto.PagamentoPix.Id));
        Assert.Equal(auditoriaAntes, await ObterSnapshotAuditoriaAsync(contexto.PagamentoPix.Id));
        Assert.Equal(cashbackAntes.Valor, cashbackPersistido.Valor);
        Assert.Equal(cashbackAntes.UsuarioIndicadorId, cashbackPersistido.UsuarioIndicadorId);
        Assert.Equal(cashbackAntes.CreatedAt, cashbackPersistido.CreatedAt);
    }

    [MySqlIntegrationFact]
    public async Task AplicarAsync_QuandoFalhaConfirmada_DeveAtualizarSomentePagamento()
    {
        foreach (var (tentativas, statusEsperado) in new[]
                 {
                     (1, StatusPagamentoPix.Falhou),
                     (5, StatusPagamentoPix.FalhaDefinitiva)
                 })
        {
            await fixture.LimparDadosAsync();
            var contexto = await CriarContextoPersistidoAsync(ResultadoOperacaoPagamentoPix.FalhaConfirmada, tentativas);
            var materialAntes = await ObterMaterialProtegidoAsync(contexto.PagamentoPix.Id);
            var auditoriaAntes = await ObterSnapshotAuditoriaAsync(contexto.PagamentoPix.Id);
            var cashbackAntes = await ObterSnapshotCashbackAsync(contexto.Cashback.Id);

            var resultado = await CriarService().AplicarAsync(contexto.PagamentoPix.Id, CancellationToken.None);

            var pagamentoPersistido = (await CriarPagamentoRepository()
                .ObterPorIdAsync(contexto.PagamentoPix.Id, CancellationToken.None))!;
            var cashbackPersistido = (await CriarCashbackRepository()
                .ObterPorIdAsync(contexto.Cashback.Id, CancellationToken.None))!;

            Assert.Equal(StatusAplicacaoPagamentoPix.Aplicado, resultado.Status);
            Assert.Equal(statusEsperado, pagamentoPersistido.Status);
            Assert.Equal(StatusCashback.Disponivel, cashbackPersistido.Status);
            Assert.Equal(tentativas, pagamentoPersistido.QuantidadeTentativas);
            Assert.Equal(materialAntes, await ObterMaterialProtegidoAsync(contexto.PagamentoPix.Id));
            Assert.Equal(auditoriaAntes, await ObterSnapshotAuditoriaAsync(contexto.PagamentoPix.Id));
            Assert.Equal(cashbackAntes, await ObterSnapshotCashbackAsync(contexto.Cashback.Id));
        }
    }

    [MySqlIntegrationFact]
    public async Task AplicarAsync_QuandoAtualizacaoDoCashbackFalhar_DeveReverterPagamento()
    {
        await fixture.LimparDadosAsync();
        var contexto = await CriarContextoPersistidoAsync(ResultadoOperacaoPagamentoPix.Confirmado);
        await CriarTriggerDeFalhaNoPagamentoDoCashbackAsync();

        try
        {
            await Assert.ThrowsAsync<MySqlException>(() =>
                CriarService().AplicarAsync(contexto.PagamentoPix.Id, CancellationToken.None));
        }
        finally
        {
            await RemoverTriggerDeFalhaNoPagamentoDoCashbackAsync();
        }

        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(contexto.PagamentoPix.Id, CancellationToken.None))!;
        var cashbackPersistido = (await CriarCashbackRepository()
            .ObterPorIdAsync(contexto.Cashback.Id, CancellationToken.None))!;

        Assert.Equal(StatusPagamentoPix.Processando, pagamentoPersistido.Status);
        Assert.Equal(StatusCashback.Disponivel, cashbackPersistido.Status);
    }

    [MySqlIntegrationFact]
    public async Task AplicarAsync_QuandoCincoExecutoresConcorrerem_DeveSerIdempotente()
    {
        foreach (var (resultadoOperacao, statusPagamentoEsperado, statusCashbackEsperado) in new[]
                 {
                     (ResultadoOperacaoPagamentoPix.Confirmado, StatusPagamentoPix.Concluido, StatusCashback.Pago),
                     (ResultadoOperacaoPagamentoPix.FalhaConfirmada, StatusPagamentoPix.Falhou, StatusCashback.Disponivel)
                 })
        {
            await fixture.LimparDadosAsync();
            var contexto = await CriarContextoPersistidoAsync(resultadoOperacao);
            var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tarefas = Enumerable.Range(0, 5)
                .Select(async _ =>
                {
                    await inicio.Task;
                    return await CriarService().AplicarAsync(contexto.PagamentoPix.Id, CancellationToken.None);
                })
                .ToArray();

            inicio.SetResult();
            var resultados = await Task.WhenAll(tarefas);
            var pagamentoPersistido = (await CriarPagamentoRepository()
                .ObterPorIdAsync(contexto.PagamentoPix.Id, CancellationToken.None))!;
            var cashbackPersistido = (await CriarCashbackRepository()
                .ObterPorIdAsync(contexto.Cashback.Id, CancellationToken.None))!;

            Assert.Equal(1, resultados.Count(resultado => resultado.Status == StatusAplicacaoPagamentoPix.Aplicado));
            Assert.Equal(4, resultados.Count(resultado => resultado.Status == StatusAplicacaoPagamentoPix.JaAplicado));
            Assert.Equal(statusPagamentoEsperado, pagamentoPersistido.Status);
            Assert.Equal(statusCashbackEsperado, cashbackPersistido.Status);
        }
    }

    [MySqlIntegrationFact]
    public async Task AplicarAsync_QuandoExecutadoNovamenteAposConfirmacao_DeveRetornarJaAplicadoSemAlterarAuditoria()
    {
        await fixture.LimparDadosAsync();
        var contexto = await CriarContextoPersistidoAsync(ResultadoOperacaoPagamentoPix.Confirmado);
        var service = CriarService();
        _ = await service.AplicarAsync(contexto.PagamentoPix.Id, CancellationToken.None);
        var auditoriaAntes = await ObterSnapshotAuditoriaAsync(contexto.PagamentoPix.Id);

        var resultado = await service.AplicarAsync(contexto.PagamentoPix.Id, CancellationToken.None);

        Assert.Equal(StatusAplicacaoPagamentoPix.JaAplicado, resultado.Status);
        Assert.Equal(auditoriaAntes, await ObterSnapshotAuditoriaAsync(contexto.PagamentoPix.Id));
    }

    [MySqlIntegrationFact]
    public async Task AplicarAsync_QuandoResultadoConclusivoForDeTentativaAnterior_NaoDeveAplicarCicloAtual()
    {
        await fixture.LimparDadosAsync();
        var contexto = await CriarContextoPersistidoAsync(ResultadoOperacaoPagamentoPix.Pendente, 2);
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var envioAnterior = OperacaoPagamentoPix.IniciarEnvio(contexto.PagamentoPix.Id, 1);
        await AdicionarEFinalizarAsync(operacaoRepository, envioAnterior, ResultadoOperacaoPagamentoPix.Confirmado);

        var resultado = await CriarService().AplicarAsync(contexto.PagamentoPix.Id, CancellationToken.None);
        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(contexto.PagamentoPix.Id, CancellationToken.None))!;
        var cashbackPersistido = (await CriarCashbackRepository()
            .ObterPorIdAsync(contexto.Cashback.Id, CancellationToken.None))!;

        Assert.Equal(StatusAplicacaoPagamentoPix.SemResultadoConclusivo, resultado.Status);
        Assert.Equal(StatusPagamentoPix.Processando, pagamentoPersistido.Status);
        Assert.Equal(StatusCashback.Disponivel, cashbackPersistido.Status);
    }

    [MySqlIntegrationFact]
    public async Task AplicarAsync_QuandoEvidenciasConclusivasConflitarem_NaoDeveAlterarEstadoFinanceiro()
    {
        await fixture.LimparDadosAsync();
        var contexto = await CriarContextoPersistidoAsync(ResultadoOperacaoPagamentoPix.Confirmado);
        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var consulta = OperacaoPagamentoPix.IniciarConsulta(contexto.PagamentoPix.Id);
        await AdicionarEFinalizarAsync(operacaoRepository, consulta, ResultadoOperacaoPagamentoPix.FalhaConfirmada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CriarService().AplicarAsync(contexto.PagamentoPix.Id, CancellationToken.None));

        var pagamentoPersistido = (await CriarPagamentoRepository()
            .ObterPorIdAsync(contexto.PagamentoPix.Id, CancellationToken.None))!;
        var cashbackPersistido = (await CriarCashbackRepository()
            .ObterPorIdAsync(contexto.Cashback.Id, CancellationToken.None))!;
        Assert.Equal(StatusPagamentoPix.Processando, pagamentoPersistido.Status);
        Assert.Equal(StatusCashback.Disponivel, cashbackPersistido.Status);
    }

    private PagamentoPixAplicacaoResultadoService CriarService() =>
        new(
            CriarPagamentoRepository(),
            CriarCashbackRepository(),
            new PagamentoPixAplicacaoResultadoMySqlStore(fixture.ConnectionFactory));

    private async Task<ContextoFinanceiro> CriarContextoPersistidoAsync(
        ResultadoOperacaoPagamentoPix resultadoOperacao,
        int tentativas = 1)
    {
        var usuarioRepository = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistoriaRepository = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var indicacaoRepository = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var pagamentoVistoriaRepository = new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        var cashbackRepository = CriarCashbackRepository();
        var indicador = IntegrationTestData.CriarUsuario();
        var indicada = IntegrationTestData.CriarUsuario();
        await usuarioRepository.AdicionarAsync(indicador, CancellationToken.None);
        await usuarioRepository.AdicionarAsync(indicada, CancellationToken.None);
        var vistoria = IntegrationTestData.CriarVistoria(indicada.Id);
        await vistoriaRepository.AdicionarAsync(vistoria, CancellationToken.None);
        var indicacao = new Indicacao(indicador.Id, "Indicada Resultado", "11999999999", indicador.CodigoIndicacao!);
        indicacao.VincularVistoria(vistoria.Id);
        await indicacaoRepository.AdicionarAsync(indicacao, CancellationToken.None);
        var pagamentoVistoria = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id);
        pagamentoVistoria.Confirmar();
        await pagamentoVistoriaRepository.AdicionarAsync(pagamentoVistoria, CancellationToken.None);
        var cashback = Cashback.Criar(indicacao.Id, pagamentoVistoria.Id, indicador.Id, pagamentoVistoria.Valor);
        cashback.Aprovar();
        await cashbackRepository.AdicionarAsync(cashback, CancellationToken.None);

        var pagamentoCriado = PagamentoPix.Criar(
            cashback.Id,
            indicador.Id,
            cashback.Valor,
            TipoChavePix.Email,
            "snapshot@exemplo.com");
        var pagamentoPix = PagamentoPix.Reidratar(
            pagamentoCriado.Id,
            pagamentoCriado.CashbackId,
            pagamentoCriado.UsuarioBeneficiarioId,
            pagamentoCriado.Valor,
            pagamentoCriado.TipoChavePix,
            pagamentoCriado.ChavePix,
            StatusPagamentoPix.Processando,
            tentativas,
            pagamentoCriado.CreatedAt,
            pagamentoCriado.UpdatedAt);
        await CriarPagamentoRepository().AdicionarAsync(pagamentoPix, CancellationToken.None);

        var operacaoRepository = new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory);
        var envio = OperacaoPagamentoPix.IniciarEnvio(pagamentoPix.Id, tentativas);
        await AdicionarEFinalizarAsync(operacaoRepository, envio, resultadoOperacao);

        return new ContextoFinanceiro(pagamentoPix, cashback);
    }

    private static async Task AdicionarEFinalizarAsync(
        IOperacaoPagamentoPixRepository operacaoRepository,
        OperacaoPagamentoPix operacao,
        ResultadoOperacaoPagamentoPix resultado)
    {
        await operacaoRepository.AdicionarAsync(operacao, CancellationToken.None);
        operacao.Finalizar(resultado, "provider-id", "provider-code");
        Assert.True(await operacaoRepository.FinalizarAsync(operacao, CancellationToken.None));
    }

    private async Task CriarTriggerDeFalhaNoPagamentoDoCashbackAsync()
    {
        const string sql = """
            CREATE TRIGGER tr_bloquear_pagamento_cashback
            BEFORE UPDATE ON cashbacks
            FOR EACH ROW
            BEGIN
                IF NEW.status = 2 THEN
                    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'falha induzida para validar rollback';
                END IF;
            END;
            """;
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RemoverTriggerDeFalhaNoPagamentoDoCashbackAsync()
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "DROP TRIGGER IF EXISTS tr_bloquear_pagamento_cashback;",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private PagamentoPixMySqlRepository CriarPagamentoRepository() =>
        new(fixture.ConnectionFactory, new AesGcmDadosPixProtector(CriarChave()));

    private CashbackMySqlRepository CriarCashbackRepository() => new(fixture.ConnectionFactory);

    private async Task<string> ObterMaterialProtegidoAsync(Guid pagamentoPixId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT CONCAT(HEX(chave_pix_ciphertext), ':', HEX(chave_pix_nonce), ':', HEX(chave_pix_tag), ':', encryption_version) FROM pagamentos_pix WHERE id = @id;",
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = pagamentoPixId.ToString();
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> ObterSnapshotAuditoriaAsync(Guid pagamentoPixId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT GROUP_CONCAT(CONCAT(id, ':', resultado, ':', COALESCE(DATE_FORMAT(finished_at, '%Y-%m-%dT%H:%i:%s.%f'), 'NULL')) ORDER BY started_at, id SEPARATOR '|') FROM operacoes_pagamento_pix WHERE pagamento_pix_id = @id;",
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = pagamentoPixId.ToString();
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<SnapshotCashback> ObterSnapshotCashbackAsync(Guid cashbackId)
    {
        await using var connection = fixture.ConnectionFactory.Create();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT usuario_indicador_id, valor, created_at FROM cashbacks WHERE id = @id;",
            connection);
        command.Parameters.Add("@id", MySqlDbType.VarChar).Value = cashbackId.ToString();
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new SnapshotCashback(
            Guid.Parse(reader.GetString(0)),
            reader.GetDecimal(1),
            DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc));
    }

    private static string CriarChave() =>
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(valor => (byte)valor).ToArray());

    private sealed record ContextoFinanceiro(PagamentoPix PagamentoPix, Cashback Cashback);

    private sealed record SnapshotCashback(Guid UsuarioIndicadorId, decimal Valor, DateTime CreatedAt);
}

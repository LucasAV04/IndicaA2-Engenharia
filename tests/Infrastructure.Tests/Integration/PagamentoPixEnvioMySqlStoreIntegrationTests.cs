using Application.Interfaces.Providers;
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
    }

    private PagamentoPixEnvioService CriarOrquestrador(IPixProvider provider) =>
        new(
            CriarPagamentoRepository(),
            new PagamentoPixEnvioMySqlStore(fixture.ConnectionFactory),
            new OperacaoPagamentoPixMySqlRepository(fixture.ConnectionFactory),
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

    private static string CriarChave() =>
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(valor => (byte)valor).ToArray());

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
}

using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Infrastructure.Repositories;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PagamentoVistoriaMySqlRepositoryIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task AdicionarEObterPorIdAsync_DevePreservarValorEstadoEDatasEmUtc()
    {
        await fixture.LimparDadosAsync();
        var (repository, vistoria) = await CriarRepositoryComVistoriaAsync();
        var pagamento = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id, 499.90m);

        await repository.AdicionarAsync(pagamento, CancellationToken.None);
        var persistido = await repository.ObterPorIdAsync(pagamento.Id, CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.Equal(pagamento.Id, persistido.Id);
        Assert.Equal(vistoria.Id, persistido.VistoriaId);
        Assert.Equal(499.90m, persistido.Valor);
        Assert.Equal(StatusPagamentoVistoria.Pendente, persistido.Status);
        Assert.Null(persistido.PagoEm);
        Assert.Equal(pagamento.CreatedAt, persistido.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(pagamento.UpdatedAt, persistido.UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(DateTimeKind.Utc, persistido.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, persistido.UpdatedAt.Kind);
    }

    [MySqlIntegrationFact]
    public async Task ObterPorVistoriaIdAsync_DeveRetornarPagamentoAssociado()
    {
        await fixture.LimparDadosAsync();
        var (repository, vistoria) = await CriarRepositoryComVistoriaAsync();
        var pagamento = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id);
        await repository.AdicionarAsync(pagamento, CancellationToken.None);

        var persistido = await repository.ObterPorVistoriaIdAsync(vistoria.Id, CancellationToken.None);
        var inexistente = await repository.ObterPorVistoriaIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.Equal(pagamento.Id, persistido.Id);
        Assert.Null(inexistente);
    }

    [MySqlIntegrationFact]
    public async Task ObterTodosAsync_DeveRetornarPagamentosDeVistoriasDistintas()
    {
        await fixture.LimparDadosAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistorias = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var repository = new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        var usuario = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(usuario, CancellationToken.None);
        var primeiraVistoria = IntegrationTestData.CriarVistoria(usuario.Id);
        var segundaVistoria = IntegrationTestData.CriarVistoria(usuario.Id, 85.50m);
        await vistorias.AdicionarAsync(primeiraVistoria, CancellationToken.None);
        await vistorias.AdicionarAsync(segundaVistoria, CancellationToken.None);
        var primeiroPagamento = IntegrationTestData.CriarPagamentoVistoria(primeiraVistoria.Id, 200m);
        var segundoPagamento = IntegrationTestData.CriarPagamentoVistoria(segundaVistoria.Id, 300m);
        await repository.AdicionarAsync(primeiroPagamento, CancellationToken.None);
        await repository.AdicionarAsync(segundoPagamento, CancellationToken.None);

        var pagamentos = await repository.ObterTodosAsync(CancellationToken.None);

        Assert.Contains(pagamentos, pagamento => pagamento.Id == primeiroPagamento.Id);
        Assert.Contains(pagamentos, pagamento => pagamento.Id == segundoPagamento.Id);
    }

    [MySqlIntegrationFact]
    public async Task AtualizarAsync_QuandoConfirmado_DevePreservarValorVistoriaEPagoEm()
    {
        await fixture.LimparDadosAsync();
        var (repository, vistoria) = await CriarRepositoryComVistoriaAsync();
        var pagamento = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id);
        await repository.AdicionarAsync(pagamento, CancellationToken.None);

        pagamento.Confirmar();
        await repository.AtualizarAsync(pagamento, CancellationToken.None);
        var persistido = await repository.ObterPorIdAsync(pagamento.Id, CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.Equal(StatusPagamentoVistoria.Confirmado, persistido.Status);
        Assert.NotNull(persistido.PagoEm);
        Assert.Equal(pagamento.PagoEm!.Value, persistido.PagoEm!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(DateTimeKind.Utc, persistido.PagoEm.Value.Kind);
        Assert.Equal(pagamento.UpdatedAt, persistido.UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(pagamento.Valor, persistido.Valor);
        Assert.Equal(vistoria.Id, persistido.VistoriaId);
    }

    [MySqlIntegrationFact]
    public async Task AtualizarAsync_QuandoCancelado_DevePreservarPagoEmNulo()
    {
        await fixture.LimparDadosAsync();
        var (repository, vistoria) = await CriarRepositoryComVistoriaAsync();
        var pagamento = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id);
        await repository.AdicionarAsync(pagamento, CancellationToken.None);

        pagamento.Cancelar();
        await repository.AtualizarAsync(pagamento, CancellationToken.None);
        var persistido = await repository.ObterPorIdAsync(pagamento.Id, CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.Equal(StatusPagamentoVistoria.Cancelado, persistido.Status);
        Assert.Null(persistido.PagoEm);
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoVistoriaJaPossuirPagamento_DeveTraduzirViolacaoDaConstraintEspecifica()
    {
        await fixture.LimparDadosAsync();
        var (repository, vistoria) = await CriarRepositoryComVistoriaAsync();
        await repository.AdicionarAsync(IntegrationTestData.CriarPagamentoVistoria(vistoria.Id), CancellationToken.None);

        await Assert.ThrowsAsync<PagamentoVistoriaDuplicadoException>(() =>
            repository.AdicionarAsync(IntegrationTestData.CriarPagamentoVistoria(vistoria.Id), CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoOutraConstraintDuplicateKeyForViolada_NaoDeveMascararMySqlException()
    {
        await fixture.LimparDadosAsync();
        var (repository, primeiraVistoria) = await CriarRepositoryComVistoriaAsync();
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistorias = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var usuario = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(usuario, CancellationToken.None);
        var segundaVistoria = IntegrationTestData.CriarVistoria(usuario.Id, 85.50m);
        await vistorias.AdicionarAsync(segundaVistoria, CancellationToken.None);
        var primeiroPagamento = IntegrationTestData.CriarPagamentoVistoria(primeiraVistoria.Id);
        await repository.AdicionarAsync(primeiroPagamento, CancellationToken.None);
        var pagamentoComIdDuplicado = PagamentoVistoria.Reidratar(
            primeiroPagamento.Id,
            segundaVistoria.Id,
            300m,
            StatusPagamentoVistoria.Pendente,
            null,
            primeiroPagamento.CreatedAt,
            primeiroPagamento.UpdatedAt);

        await Assert.ThrowsAsync<MySqlException>(() =>
            repository.AdicionarAsync(pagamentoComIdDuplicado, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoVistoriaNaoExistir_DeveRespeitarFkDoMySql()
    {
        await fixture.LimparDadosAsync();
        var repository = new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);

        await Assert.ThrowsAsync<MySqlException>(() =>
            repository.AdicionarAsync(IntegrationTestData.CriarPagamentoVistoria(Guid.NewGuid()), CancellationToken.None));
    }

    private async Task<(PagamentoVistoriaMySqlRepository Repository, Vistoria Vistoria)> CriarRepositoryComVistoriaAsync()
    {
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistorias = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var repository = new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        var usuario = IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(usuario, CancellationToken.None);
        var vistoria = IntegrationTestData.CriarVistoria(usuario.Id);
        await vistorias.AdicionarAsync(vistoria, CancellationToken.None);

        return (repository, vistoria);
    }
}

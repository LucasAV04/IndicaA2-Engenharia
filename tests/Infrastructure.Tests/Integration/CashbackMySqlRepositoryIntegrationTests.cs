using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Infrastructure.Repositories;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CashbackMySqlRepositoryIntegrationTests(MySqlIntegrationFixture fixture)
{
    [MySqlIntegrationFact]
    public async Task AdicionarEObterPorIdAsync_DevePreservarSnapshotsStatusEDatasEmUtc()
    {
        await fixture.LimparDadosAsync();
        var (repository, cashback) = await CriarCashbackAsync();

        await repository.AdicionarAsync(cashback, CancellationToken.None);
        var persistido = await repository.ObterPorIdAsync(cashback.Id, CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.Equal(cashback.Id, persistido.Id);
        Assert.Equal(cashback.IndicacaoId, persistido.IndicacaoId);
        Assert.Equal(cashback.PagamentoVistoriaId, persistido.PagamentoVistoriaId);
        Assert.Equal(cashback.UsuarioIndicadorId, persistido.UsuarioIndicadorId);
        Assert.Equal(cashback.ValorTotalPago, persistido.ValorTotalPago);
        Assert.Equal(cashback.Percentual, persistido.Percentual);
        Assert.Equal(cashback.Valor, persistido.Valor);
        Assert.Equal(StatusCashback.Pendente, persistido.Status);
        Assert.Equal(cashback.CreatedAt, persistido.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(cashback.UpdatedAt, persistido.UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(DateTimeKind.Utc, persistido.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, persistido.UpdatedAt.Kind);
    }

    [MySqlIntegrationFact]
    public async Task ObterPorPagamentoVistoriaIdAsync_DeveRetornarCashbackAssociado()
    {
        await fixture.LimparDadosAsync();
        var (repository, cashback) = await CriarCashbackAsync();
        await repository.AdicionarAsync(cashback, CancellationToken.None);

        var persistido = await repository.ObterPorPagamentoVistoriaIdAsync(cashback.PagamentoVistoriaId, CancellationToken.None);
        var inexistente = await repository.ObterPorPagamentoVistoriaIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.Equal(cashback.Id, persistido.Id);
        Assert.Null(inexistente);
    }

    [MySqlIntegrationFact]
    public async Task ObterPorUsuarioIndicadorIdEObterTodosAsync_DevemRetornarCashbacksPersistidos()
    {
        await fixture.LimparDadosAsync();
        var (repository, primeiroCashback) = await CriarCashbackAsync();
        await repository.AdicionarAsync(primeiroCashback, CancellationToken.None);
        var (_, segundoCashback) = await CriarCashbackAsync(primeiroCashback.UsuarioIndicadorId);
        await repository.AdicionarAsync(segundoCashback, CancellationToken.None);

        var porIndicador = await repository.ObterPorUsuarioIndicadorIdAsync(primeiroCashback.UsuarioIndicadorId, CancellationToken.None);
        var todos = await repository.ObterTodosAsync(CancellationToken.None);

        Assert.Equal(2, porIndicador.Count);
        Assert.Contains(porIndicador, cashback => cashback.Id == primeiroCashback.Id);
        Assert.Contains(porIndicador, cashback => cashback.Id == segundoCashback.Id);
        Assert.Contains(todos, cashback => cashback.Id == primeiroCashback.Id);
        Assert.Contains(todos, cashback => cashback.Id == segundoCashback.Id);
    }

    [MySqlIntegrationFact]
    public async Task AtualizarAsync_QuandoAprovado_DeveAlterarSomenteStatusEUpdatedAt()
    {
        await fixture.LimparDadosAsync();
        var (repository, cashback) = await CriarCashbackAsync();
        await repository.AdicionarAsync(cashback, CancellationToken.None);
        var snapshots = (cashback.IndicacaoId, cashback.PagamentoVistoriaId, cashback.UsuarioIndicadorId,
            cashback.ValorTotalPago, cashback.Percentual, cashback.Valor);

        cashback.Aprovar();
        await repository.AtualizarAsync(cashback, CancellationToken.None);
        var persistido = await repository.ObterPorIdAsync(cashback.Id, CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.Equal(StatusCashback.Disponivel, persistido.Status);
        Assert.Equal(snapshots, (persistido.IndicacaoId, persistido.PagamentoVistoriaId, persistido.UsuarioIndicadorId,
            persistido.ValorTotalPago, persistido.Percentual, persistido.Valor));
        Assert.Equal(cashback.UpdatedAt, persistido.UpdatedAt, TimeSpan.FromMilliseconds(1));
    }

    [MySqlIntegrationFact]
    public async Task AtualizarAsync_QuandoCancelado_DevePersistirStatusSemAlterarSnapshots()
    {
        await fixture.LimparDadosAsync();
        var (repository, cashback) = await CriarCashbackAsync();
        await repository.AdicionarAsync(cashback, CancellationToken.None);
        cashback.Cancelar();

        await repository.AtualizarAsync(cashback, CancellationToken.None);
        var persistido = await repository.ObterPorIdAsync(cashback.Id, CancellationToken.None);

        Assert.NotNull(persistido);
        Assert.Equal(StatusCashback.Cancelado, persistido.Status);
        Assert.Equal(cashback.ValorTotalPago, persistido.ValorTotalPago);
        Assert.Equal(cashback.Percentual, persistido.Percentual);
        Assert.Equal(cashback.Valor, persistido.Valor);
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoPagamentoJaPossuirCashback_DeveTraduzirConstraintEspecifica()
    {
        await fixture.LimparDadosAsync();
        var (repository, cashback) = await CriarCashbackAsync();
        await repository.AdicionarAsync(cashback, CancellationToken.None);

        var duplicado = Cashback.Reidratar(
            Guid.NewGuid(), cashback.IndicacaoId, cashback.PagamentoVistoriaId, cashback.UsuarioIndicadorId,
            cashback.ValorTotalPago, cashback.Percentual, cashback.Valor, StatusCashback.Pendente,
            cashback.CreatedAt, cashback.UpdatedAt);

        await Assert.ThrowsAsync<CashbackJaExisteException>(() =>
            repository.AdicionarAsync(duplicado, CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task AdicionarAsync_QuandoOutraDuplicateKeyForViolada_NaoDeveMascararMySqlException()
    {
        await fixture.LimparDadosAsync();
        var (repository, primeiroCashback) = await CriarCashbackAsync();
        await repository.AdicionarAsync(primeiroCashback, CancellationToken.None);
        var (_, segundoCashback) = await CriarCashbackAsync(primeiroCashback.UsuarioIndicadorId);
        var idDuplicado = Cashback.Reidratar(
            primeiroCashback.Id, segundoCashback.IndicacaoId, segundoCashback.PagamentoVistoriaId,
            segundoCashback.UsuarioIndicadorId, segundoCashback.ValorTotalPago, segundoCashback.Percentual,
            segundoCashback.Valor, StatusCashback.Pendente, segundoCashback.CreatedAt, segundoCashback.UpdatedAt);

        await Assert.ThrowsAsync<MySqlException>(() => repository.AdicionarAsync(idDuplicado, CancellationToken.None));
    }

    private async Task<(CashbackMySqlRepository Repository, Cashback Cashback)> CriarCashbackAsync(Guid? usuarioIndicadorId = null)
    {
        var usuarios = new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var vistorias = new VistoriaMySqlRepository(fixture.ConnectionFactory);
        var indicacoes = new IndicacaoMySqlRepository(fixture.ConnectionFactory);
        var pagamentos = new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        var repository = new CashbackMySqlRepository(fixture.ConnectionFactory);
        Usuario usuario;

        if (usuarioIndicadorId.HasValue)
        {
            usuario = await usuarios.ObterPorIdAsync(usuarioIndicadorId.Value, CancellationToken.None)
                ?? throw new InvalidOperationException("O usuário indicador de teste deveria existir.");
        }
        else
        {
            usuario = IntegrationTestData.CriarUsuario();
            await usuarios.AdicionarAsync(usuario, CancellationToken.None);
        }

        var vistoria = IntegrationTestData.CriarVistoria(usuario.Id);
        await vistorias.AdicionarAsync(vistoria, CancellationToken.None);
        var indicacao = new Indicacao(usuario.Id, "Pessoa Indicada", "11988887777", usuario.CodigoIndicacao!);
        indicacao.VincularVistoria(vistoria.Id);
        await indicacoes.AdicionarAsync(indicacao, CancellationToken.None);
        var pagamento = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id);
        pagamento.Confirmar();
        await pagamentos.AdicionarAsync(pagamento, CancellationToken.None);

        return (repository, Cashback.Criar(indicacao.Id, pagamento.Id, usuario.Id, pagamento.Valor));
    }
}

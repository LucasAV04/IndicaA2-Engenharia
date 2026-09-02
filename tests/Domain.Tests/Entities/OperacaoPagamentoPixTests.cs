using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class OperacaoPagamentoPixTests
{
    [Fact]
    public void IniciarEnvio_DeveDerivarReferenciaEExigirTentativa()
    {
        var pagamentoPixId = Guid.NewGuid();

        var operacao = OperacaoPagamentoPix.IniciarEnvio(pagamentoPixId, 2);

        Assert.Equal(pagamentoPixId, operacao.PagamentoPixId);
        Assert.Equal(TipoOperacaoPagamentoPix.Envio, operacao.TipoOperacao);
        Assert.Equal(2, operacao.NumeroTentativaEnvio);
        Assert.Equal(pagamentoPixId.ToString("N"), operacao.ReferenciaIdempotente);
        Assert.Null(operacao.Resultado);
        Assert.Null(operacao.FinishedAt);
        Assert.Equal(DateTimeKind.Utc, operacao.CreatedAt.Kind);
    }

    [Fact]
    public void IniciarConsulta_NaoDeveInventarTentativaFinanceira()
    {
        var pagamentoPixId = Guid.NewGuid();

        var operacao = OperacaoPagamentoPix.IniciarConsulta(pagamentoPixId);

        Assert.Equal(TipoOperacaoPagamentoPix.Consulta, operacao.TipoOperacao);
        Assert.Null(operacao.NumeroTentativaEnvio);
        Assert.Equal(pagamentoPixId.ToString("N"), operacao.ReferenciaIdempotente);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void IniciarEnvio_QuandoTentativaNaoForCompativelComPagamentoPix_DeveRejeitar(int tentativa) =>
        Assert.Throws<ArgumentException>(() => OperacaoPagamentoPix.IniciarEnvio(Guid.NewGuid(), tentativa));

    [Theory]
    [InlineData(ResultadoOperacaoPagamentoPix.Confirmado)]
    [InlineData(ResultadoOperacaoPagamentoPix.FalhaConfirmada)]
    [InlineData(ResultadoOperacaoPagamentoPix.Pendente)]
    [InlineData(ResultadoOperacaoPagamentoPix.Indeterminado)]
    public void Finalizar_DevePreservarResultadoProviderAgnostic(ResultadoOperacaoPagamentoPix resultado)
    {
        var operacao = OperacaoPagamentoPix.IniciarEnvio(Guid.NewGuid(), 1);
        var camposImutaveis = (operacao.PagamentoPixId, operacao.TipoOperacao, operacao.NumeroTentativaEnvio, operacao.ReferenciaIdempotente);

        operacao.Finalizar(resultado, "id-opaco", "codigo-opaco");

        Assert.Equal(resultado, operacao.Resultado);
        Assert.Equal("id-opaco", operacao.IdentificadorProvider);
        Assert.Equal("codigo-opaco", operacao.Codigo);
        Assert.NotNull(operacao.FinishedAt);
        Assert.Equal(camposImutaveis, (operacao.PagamentoPixId, operacao.TipoOperacao, operacao.NumeroTentativaEnvio, operacao.ReferenciaIdempotente));
    }

    [Fact]
    public void Finalizar_DuasVezes_DeveRejeitarSobrescrita()
    {
        var operacao = OperacaoPagamentoPix.IniciarConsulta(Guid.NewGuid());
        operacao.Finalizar(ResultadoOperacaoPagamentoPix.Pendente);

        Assert.Throws<DomainException>(() => operacao.Finalizar(ResultadoOperacaoPagamentoPix.Confirmado));
    }

    [Fact]
    public void Reidratar_DevePreservarOperacaoFinalizadaSemExecutarTransicao()
    {
        var id = Guid.NewGuid();
        var pagamentoPixId = Guid.NewGuid();
        var inicio = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        var fim = inicio.AddMinutes(1);

        var operacao = OperacaoPagamentoPix.Reidratar(
            id, pagamentoPixId, TipoOperacaoPagamentoPix.Envio, 1, pagamentoPixId.ToString("N"),
            ResultadoOperacaoPagamentoPix.Confirmado, "id-opaco", "codigo-opaco", inicio, fim, fim);

        Assert.Equal(id, operacao.Id);
        Assert.Equal(fim, operacao.FinishedAt);
        Assert.Equal(ResultadoOperacaoPagamentoPix.Confirmado, operacao.Resultado);
        Assert.Equal(inicio, operacao.CreatedAt);
    }

    [Fact]
    public void Reidratar_QuandoCombinacaoForInvalida_DeveRejeitar()
    {
        var pagamentoPixId = Guid.NewGuid();
        var inicio = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => OperacaoPagamentoPix.Reidratar(
            Guid.NewGuid(), pagamentoPixId, TipoOperacaoPagamentoPix.Consulta, 1, pagamentoPixId.ToString("N"),
            null, null, null, inicio, inicio, null));
        Assert.Throws<ArgumentException>(() => OperacaoPagamentoPix.Reidratar(
            Guid.NewGuid(), pagamentoPixId, TipoOperacaoPagamentoPix.Envio, 1, "outra-referencia",
            null, null, null, inicio, inicio, null));
        Assert.Throws<ArgumentException>(() => OperacaoPagamentoPix.Reidratar(
            Guid.NewGuid(), pagamentoPixId, TipoOperacaoPagamentoPix.Envio, 1, pagamentoPixId.ToString("N"),
            ResultadoOperacaoPagamentoPix.Confirmado, null, null, inicio, inicio, null));
    }
}

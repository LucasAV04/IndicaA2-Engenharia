using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class CashbackTests
{
    [Fact]
    public void Criar_QuandoDadosValidos_DeveCalcularSnapshotFinanceiroEPermanecerPendente()
    {
        var indicacaoId = Guid.NewGuid();
        var pagamentoId = Guid.NewGuid();
        var indicadorId = Guid.NewGuid();

        var cashback = Cashback.Criar(indicacaoId, pagamentoId, indicadorId, 500m);

        Assert.Equal(indicacaoId, cashback.IndicacaoId);
        Assert.Equal(pagamentoId, cashback.PagamentoVistoriaId);
        Assert.Equal(indicadorId, cashback.UsuarioIndicadorId);
        Assert.Equal(500m, cashback.ValorTotalPago);
        Assert.Equal(0.20m, cashback.Percentual);
        Assert.Equal(100m, cashback.Valor);
        Assert.Equal(StatusCashback.Pendente, cashback.Status);
        Assert.Equal(DateTimeKind.Utc, cashback.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, cashback.UpdatedAt.Kind);
    }

    [Theory]
    [InlineData(499.90, 99.98)]
    [InlineData(100.13, 20.03)]
    public void Criar_DeveAplicarCalculoEMoedaComDuasCasas(decimal valorTotalPago, decimal valorEsperado)
    {
        var cashback = CriarCashback(valorTotalPago);

        Assert.Equal(valorEsperado, cashback.Valor);
    }

    [Fact]
    public void Criar_QuandoIndicacaoForVazia_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Cashback.Criar(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 500m));
    }

    [Fact]
    public void Criar_QuandoPagamentoForVazio_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Cashback.Criar(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), 500m));
    }

    [Fact]
    public void Criar_QuandoIndicadorForVazio_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Cashback.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 500m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Criar_QuandoValorTotalPagoNaoForPositivo_DeveLancarDomainException(decimal valorTotalPago)
    {
        Assert.Throws<DomainException>(() => CriarCashback(valorTotalPago));
    }

    [Fact]
    public void Aprovar_QuandoPendente_DeveDisponibilizarSemAlterarSnapshots()
    {
        var cashback = CriarCashback(499.90m);
        var updatedAtAnterior = cashback.UpdatedAt;
        var valorTotalPago = cashback.ValorTotalPago;
        var percentual = cashback.Percentual;
        var valor = cashback.Valor;

        cashback.Aprovar();

        Assert.Equal(StatusCashback.Disponivel, cashback.Status);
        Assert.Equal(valorTotalPago, cashback.ValorTotalPago);
        Assert.Equal(percentual, cashback.Percentual);
        Assert.Equal(valor, cashback.Valor);
        Assert.True(cashback.UpdatedAt >= updatedAtAnterior);
    }

    [Fact]
    public void Aprovar_QuandoJaDisponivel_DeveSerIdempotente()
    {
        var cashback = CriarCashback(500m);
        cashback.Aprovar();
        var updatedAt = cashback.UpdatedAt;

        cashback.Aprovar();

        Assert.Equal(StatusCashback.Disponivel, cashback.Status);
        Assert.Equal(updatedAt, cashback.UpdatedAt);
    }

    [Fact]
    public void Cancelar_QuandoPendente_DeveCancelarSemAlterarSnapshots()
    {
        var cashback = CriarCashback(500m);
        var valor = cashback.Valor;

        cashback.Cancelar();

        Assert.Equal(StatusCashback.Cancelado, cashback.Status);
        Assert.Equal(500m, cashback.ValorTotalPago);
        Assert.Equal(0.20m, cashback.Percentual);
        Assert.Equal(valor, cashback.Valor);
    }

    [Fact]
    public void Cancelar_QuandoDisponivel_DeveCancelar()
    {
        var cashback = CriarCashback(500m);
        cashback.Aprovar();

        cashback.Cancelar();

        Assert.Equal(StatusCashback.Cancelado, cashback.Status);
    }

    [Fact]
    public void Cancelar_QuandoJaCancelado_DeveSerIdempotente()
    {
        var cashback = CriarCashback(500m);
        cashback.Cancelar();
        var updatedAt = cashback.UpdatedAt;

        cashback.Cancelar();

        Assert.Equal(StatusCashback.Cancelado, cashback.Status);
        Assert.Equal(updatedAt, cashback.UpdatedAt);
    }

    [Fact]
    public void Aprovar_QuandoCancelado_NaoDevePermitirRetornoDeEstado()
    {
        var cashback = CriarCashback(500m);
        cashback.Cancelar();

        Assert.Throws<DomainException>(() => cashback.Aprovar());
    }

    [Fact]
    public void Reidratar_DevePreservarSnapshotsHistoricosSemRecalcular()
    {
        var createdAt = new DateTime(2025, 2, 10, 8, 30, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddDays(2);

        var cashback = Cashback.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            0.15m,
            75m,
            StatusCashback.Disponivel,
            createdAt,
            updatedAt);

        Assert.Equal(500m, cashback.ValorTotalPago);
        Assert.Equal(0.15m, cashback.Percentual);
        Assert.Equal(75m, cashback.Valor);
        Assert.Equal(StatusCashback.Disponivel, cashback.Status);
        Assert.Equal(createdAt, cashback.CreatedAt);
        Assert.Equal(updatedAt, cashback.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, cashback.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, cashback.UpdatedAt.Kind);
    }

    private static Cashback CriarCashback(decimal valorTotalPago) => Cashback.Criar(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        valorTotalPago);
}

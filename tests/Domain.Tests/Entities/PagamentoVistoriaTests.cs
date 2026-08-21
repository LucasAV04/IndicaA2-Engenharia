using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class PagamentoVistoriaTests
{
    [Fact]
    public void Construtor_QuandoDadosValidos_DeveCriarPagamentoPendenteAindaNaoConfirmado()
    {
        var vistoriaId = Guid.NewGuid();

        var pagamento = new PagamentoVistoria(vistoriaId, 500m);

        Assert.Equal(vistoriaId, pagamento.VistoriaId);
        Assert.Equal(500m, pagamento.Valor);
        Assert.Equal(StatusPagamentoVistoria.Pendente, pagamento.Status);
        Assert.Null(pagamento.PagoEm);
    }

    [Fact]
    public void Construtor_QuandoVistoriaIdForVazio_DeveLancarDomainException()
    {
        Assert.Throws<DomainException>(() => new PagamentoVistoria(Guid.Empty, 500m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    public void Construtor_QuandoValorNaoForPositivoAposNormalizacao_DeveLancarDomainException(decimal valor)
    {
        Assert.Throws<DomainException>(() => new PagamentoVistoria(Guid.NewGuid(), valor));
    }

    [Theory]
    [InlineData(10.234, 10.23)]
    [InlineData(10.235, 10.24)]
    [InlineData(10.236, 10.24)]
    public void Construtor_DeveNormalizarValorParaDuasCasasDecimais(decimal valor, decimal valorEsperado)
    {
        var pagamento = new PagamentoVistoria(Guid.NewGuid(), valor);

        Assert.Equal(valorEsperado, pagamento.Valor);
    }

    [Fact]
    public void Confirmar_QuandoPendente_DeveDefinirStatusPagoEmEUpdatedAt()
    {
        var pagamento = CriarPagamento();
        var updatedAtAnterior = pagamento.UpdatedAt;
        var antesDaConfirmacao = DateTime.UtcNow;

        pagamento.Confirmar();

        Assert.Equal(StatusPagamentoVistoria.Confirmado, pagamento.Status);
        Assert.NotNull(pagamento.PagoEm);
        Assert.True(pagamento.PagoEm >= antesDaConfirmacao);
        Assert.Equal(DateTimeKind.Utc, pagamento.PagoEm!.Value.Kind);
        Assert.True(pagamento.UpdatedAt >= updatedAtAnterior);
    }

    [Fact]
    public void Confirmar_QuandoJaConfirmado_DeveSerIdempotente()
    {
        var pagamento = CriarPagamento();
        pagamento.Confirmar();
        var pagoEm = pagamento.PagoEm;
        var updatedAt = pagamento.UpdatedAt;

        pagamento.Confirmar();

        Assert.Equal(StatusPagamentoVistoria.Confirmado, pagamento.Status);
        Assert.Equal(pagoEm, pagamento.PagoEm);
        Assert.Equal(updatedAt, pagamento.UpdatedAt);
    }

    [Fact]
    public void Cancelar_QuandoPendente_DeveDefinirStatusCanceladoSemPagoEm()
    {
        var pagamento = CriarPagamento();
        var updatedAtAnterior = pagamento.UpdatedAt;

        pagamento.Cancelar();

        Assert.Equal(StatusPagamentoVistoria.Cancelado, pagamento.Status);
        Assert.Null(pagamento.PagoEm);
        Assert.True(pagamento.UpdatedAt >= updatedAtAnterior);
    }

    [Fact]
    public void Cancelar_QuandoJaCancelado_DeveSerIdempotente()
    {
        var pagamento = CriarPagamento();
        pagamento.Cancelar();
        var updatedAt = pagamento.UpdatedAt;

        pagamento.Cancelar();

        Assert.Equal(StatusPagamentoVistoria.Cancelado, pagamento.Status);
        Assert.Equal(updatedAt, pagamento.UpdatedAt);
    }

    [Fact]
    public void TransicoesFinais_DevemImpedirMudancaParaOutroEstado()
    {
        var confirmado = CriarPagamento();
        confirmado.Confirmar();
        Assert.Throws<DomainException>(() => confirmado.Cancelar());

        var cancelado = CriarPagamento();
        cancelado.Cancelar();
        Assert.Throws<DomainException>(() => cancelado.Confirmar());
    }

    [Fact]
    public void PagamentoConfirmado_DeveManterValorSemCalcularCashback()
    {
        var pagamento = new PagamentoVistoria(Guid.NewGuid(), 500m);

        pagamento.Confirmar();

        Assert.Equal(500m, pagamento.Valor);
        Assert.Equal(StatusPagamentoVistoria.Confirmado, pagamento.Status);
    }

    private static PagamentoVistoria CriarPagamento() => new(Guid.NewGuid(), 500m);
}

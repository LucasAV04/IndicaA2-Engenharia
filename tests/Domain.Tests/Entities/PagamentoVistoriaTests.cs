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

    [Fact]
    public void Reidratar_QuandoPagamentoPendenteForValido_DevePreservarEstadoSemConfirmarRecebimento()
    {
        var id = Guid.NewGuid();
        var vistoriaId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(5);

        var pagamento = PagamentoVistoria.Reidratar(
            id,
            vistoriaId,
            499.90m,
            StatusPagamentoVistoria.Pendente,
            null,
            createdAt,
            updatedAt);

        Assert.Equal(id, pagamento.Id);
        Assert.Equal(vistoriaId, pagamento.VistoriaId);
        Assert.Equal(499.90m, pagamento.Valor);
        Assert.Equal(StatusPagamentoVistoria.Pendente, pagamento.Status);
        Assert.Null(pagamento.PagoEm);
        Assert.Equal(createdAt, pagamento.CreatedAt);
        Assert.Equal(updatedAt, pagamento.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, pagamento.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, pagamento.UpdatedAt.Kind);
    }

    [Fact]
    public void Reidratar_QuandoPagamentoConfirmadoForValido_DevePreservarPagoEmSemExecutarTransicao()
    {
        var createdAt = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var pagoEm = createdAt.AddMinutes(20);
        var updatedAt = pagoEm.AddMinutes(1);

        var pagamento = PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            StatusPagamentoVistoria.Confirmado,
            pagoEm,
            createdAt,
            updatedAt);

        Assert.Equal(StatusPagamentoVistoria.Confirmado, pagamento.Status);
        Assert.Equal(pagoEm, pagamento.PagoEm);
        Assert.Equal(updatedAt, pagamento.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, pagamento.PagoEm!.Value.Kind);
    }

    [Fact]
    public void Reidratar_QuandoPagamentoCanceladoForValido_DevePreservarEstado()
    {
        var createdAt = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

        var pagamento = PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            StatusPagamentoVistoria.Cancelado,
            null,
            createdAt,
            createdAt.AddMinutes(1));

        Assert.Equal(StatusPagamentoVistoria.Cancelado, pagamento.Status);
        Assert.Null(pagamento.PagoEm);
    }

    [Fact]
    public void Reidratar_QuandoStatusConfirmadoNaoPossuirPagoEm_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            StatusPagamentoVistoria.Confirmado,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow));
    }

    [Theory]
    [InlineData(StatusPagamentoVistoria.Pendente)]
    [InlineData(StatusPagamentoVistoria.Cancelado)]
    public void Reidratar_QuandoStatusNaoConfirmadoPossuirPagoEm_DeveLancarArgumentException(StatusPagamentoVistoria status)
    {
        var createdAt = DateTime.UtcNow;

        Assert.Throws<ArgumentException>(() => PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            status,
            createdAt.AddMinutes(1),
            createdAt,
            createdAt));
    }

    [Fact]
    public void Reidratar_QuandoEstadoPersistidoForInvalido_DeveLancarExcecaoCorrespondente()
    {
        var createdAt = DateTime.UtcNow;

        Assert.Throws<ArgumentException>(() => PagamentoVistoria.Reidratar(
            Guid.Empty,
            Guid.NewGuid(),
            500m,
            StatusPagamentoVistoria.Pendente,
            null,
            createdAt,
            createdAt));
        Assert.Throws<ArgumentException>(() => PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.Empty,
            500m,
            StatusPagamentoVistoria.Pendente,
            null,
            createdAt,
            createdAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0m,
            StatusPagamentoVistoria.Pendente,
            null,
            createdAt,
            createdAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            (StatusPagamentoVistoria)99,
            null,
            createdAt,
            createdAt));
        Assert.Throws<ArgumentException>(() => PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            StatusPagamentoVistoria.Pendente,
            null,
            default,
            createdAt));
        Assert.Throws<ArgumentException>(() => PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            StatusPagamentoVistoria.Pendente,
            null,
            createdAt,
            default));
        Assert.Throws<ArgumentException>(() => PagamentoVistoria.Reidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            StatusPagamentoVistoria.Pendente,
            null,
            createdAt,
            createdAt.AddTicks(-1)));
    }

    private static PagamentoVistoria CriarPagamento() => new(Guid.NewGuid(), 500m);
}

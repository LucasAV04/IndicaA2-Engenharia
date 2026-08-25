using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class PagamentoPixTests
{
    [Fact]
    public void Criar_QuandoSnapshotsForemValidos_DeveNascerPendenteSemTentativas()
    {
        var cashbackId = Guid.NewGuid();
        var beneficiarioId = Guid.NewGuid();

        var pagamentoPix = PagamentoPix.Criar(
            cashbackId,
            beneficiarioId,
            99.98m,
            TipoChavePix.Email,
            "indicador@exemplo.com");

        Assert.Equal(cashbackId, pagamentoPix.CashbackId);
        Assert.Equal(beneficiarioId, pagamentoPix.UsuarioBeneficiarioId);
        Assert.Equal(99.98m, pagamentoPix.Valor);
        Assert.Equal(TipoChavePix.Email, pagamentoPix.TipoChavePix);
        Assert.Equal("indicador@exemplo.com", pagamentoPix.ChavePix);
        Assert.Equal(StatusPagamentoPix.Pendente, pagamentoPix.Status);
        Assert.Equal(0, pagamentoPix.QuantidadeTentativas);
        Assert.Equal(DateTimeKind.Utc, pagamentoPix.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, pagamentoPix.UpdatedAt.Kind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Criar_QuandoValorNaoForPositivo_DeveRejeitar(decimal valor)
    {
        Assert.Throws<DomainException>(() => Criar(valor: valor));
    }

    [Fact]
    public void Criar_QuandoSnapshotsObrigatoriosForemInvalidos_DeveRejeitar()
    {
        Assert.Throws<DomainException>(() => Criar(cashbackId: Guid.Empty));
        Assert.Throws<DomainException>(() => Criar(beneficiarioId: Guid.Empty));
        Assert.Throws<DomainException>(() => Criar(tipoChavePix: (TipoChavePix)99));
        Assert.Throws<DomainException>(() => Criar(chavePix: " "));
    }

    [Fact]
    public void Criar_DeveManterSnapshotQuandoDadosPixForemAlteradosPosteriormente()
    {
        var dadosPix = new DadosPix(Guid.NewGuid(), TipoChavePix.Email, "indicador@exemplo.com");
        var pagamentoPix = PagamentoPix.Criar(
            Guid.NewGuid(),
            dadosPix.UsuarioId,
            100m,
            dadosPix.TipoChavePix,
            dadosPix.ChavePix);

        dadosPix.Atualizar(TipoChavePix.Telefone, "+55 (11) 99876-5432");

        Assert.Equal(TipoChavePix.Email, pagamentoPix.TipoChavePix);
        Assert.Equal("indicador@exemplo.com", pagamentoPix.ChavePix);
    }

    [Fact]
    public void IniciarTentativaERegistrarFalha_DeveContabilizarNoInicioEAtingirFalhaDefinitivaNaQuintaFalha()
    {
        var pagamentoPix = Criar();

        for (var tentativa = 1; tentativa <= PagamentoPix.TentativasMaximas; tentativa++)
        {
            pagamentoPix.IniciarTentativa();

            Assert.Equal(StatusPagamentoPix.Processando, pagamentoPix.Status);
            Assert.Equal(tentativa, pagamentoPix.QuantidadeTentativas);

            pagamentoPix.RegistrarFalha();

            var statusEsperado = tentativa == PagamentoPix.TentativasMaximas
                ? StatusPagamentoPix.FalhaDefinitiva
                : StatusPagamentoPix.Falhou;
            Assert.Equal(statusEsperado, pagamentoPix.Status);
            Assert.Equal(tentativa, pagamentoPix.QuantidadeTentativas);
        }

        Assert.Throws<LimiteTentativasPagamentoPixAtingidoException>(
            () => pagamentoPix.IniciarTentativa());
        Assert.Equal(PagamentoPix.TentativasMaximas, pagamentoPix.QuantidadeTentativas);
    }

    [Fact]
    public void ConfirmarConclusao_QuandoProcessando_DeveConcluirSemAlterarSnapshots()
    {
        var pagamentoPix = Criar();
        var chavePix = pagamentoPix.ChavePix;
        var updatedAtAnterior = pagamentoPix.UpdatedAt;
        pagamentoPix.IniciarTentativa();

        pagamentoPix.ConfirmarConclusao();

        Assert.Equal(StatusPagamentoPix.Concluido, pagamentoPix.Status);
        Assert.Equal(1, pagamentoPix.QuantidadeTentativas);
        Assert.Equal(chavePix, pagamentoPix.ChavePix);
        Assert.True(pagamentoPix.UpdatedAt >= updatedAtAnterior);
        Assert.Throws<TransicaoPagamentoPixInvalidaException>(() => pagamentoPix.IniciarTentativa());
    }

    [Fact]
    public void ConfirmarConclusao_QuandoNaoEstiverProcessando_DeveRejeitar()
    {
        var pendente = Criar();
        var falhou = Criar();
        falhou.IniciarTentativa();
        falhou.RegistrarFalha();
        var falhaDefinitiva = CriarComFalhaDefinitiva();
        var cancelado = Criar();
        cancelado.Cancelar();

        Assert.Throws<TransicaoPagamentoPixInvalidaException>(() => pendente.ConfirmarConclusao());
        Assert.Throws<TransicaoPagamentoPixInvalidaException>(() => falhou.ConfirmarConclusao());
        Assert.Throws<TransicaoPagamentoPixInvalidaException>(() => falhaDefinitiva.ConfirmarConclusao());
        Assert.Throws<TransicaoPagamentoPixInvalidaException>(() => cancelado.ConfirmarConclusao());
    }

    [Fact]
    public void Cancelar_QuandoPendenteOuFalhou_DevePermitirEQuandoRepetidoDeveSerIdempotente()
    {
        var pendente = Criar();
        pendente.Cancelar();

        var falhou = Criar();
        falhou.IniciarTentativa();
        falhou.RegistrarFalha();
        falhou.Cancelar();
        var updatedAt = falhou.UpdatedAt;
        falhou.Cancelar();

        Assert.Equal(StatusPagamentoPix.Cancelado, pendente.Status);
        Assert.Equal(StatusPagamentoPix.Cancelado, falhou.Status);
        Assert.Equal(updatedAt, falhou.UpdatedAt);
    }

    [Fact]
    public void Cancelar_QuandoProcessandoConcluidoOuFalhaDefinitiva_DeveRejeitar()
    {
        var processando = Criar();
        processando.IniciarTentativa();
        var concluido = Criar();
        concluido.IniciarTentativa();
        concluido.ConfirmarConclusao();
        var falhaDefinitiva = CriarComFalhaDefinitiva();

        Assert.Throws<TransicaoPagamentoPixInvalidaException>(() => processando.Cancelar());
        Assert.Throws<TransicaoPagamentoPixInvalidaException>(() => concluido.Cancelar());
        Assert.Throws<TransicaoPagamentoPixInvalidaException>(() => falhaDefinitiva.Cancelar());
    }

    [Theory]
    [InlineData(StatusPagamentoPix.Pendente, 0)]
    [InlineData(StatusPagamentoPix.Processando, 1)]
    [InlineData(StatusPagamentoPix.Processando, 5)]
    [InlineData(StatusPagamentoPix.Falhou, 1)]
    [InlineData(StatusPagamentoPix.FalhaDefinitiva, 5)]
    [InlineData(StatusPagamentoPix.Concluido, 1)]
    [InlineData(StatusPagamentoPix.Cancelado, 0)]
    [InlineData(StatusPagamentoPix.Cancelado, 1)]
    public void Reidratar_QuandoEstadoETentativasForemCoerentes_DevePreservarSnapshot(
        StatusPagamentoPix status,
        int quantidadeTentativas)
    {
        var id = Guid.NewGuid();
        var cashbackId = Guid.NewGuid();
        var beneficiarioId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(5);

        var pagamentoPix = PagamentoPix.Reidratar(
            id,
            cashbackId,
            beneficiarioId,
            99.98m,
            TipoChavePix.Email,
            "snapshot@exemplo.com",
            status,
            quantidadeTentativas,
            createdAt,
            updatedAt);

        Assert.Equal(id, pagamentoPix.Id);
        Assert.Equal(cashbackId, pagamentoPix.CashbackId);
        Assert.Equal(beneficiarioId, pagamentoPix.UsuarioBeneficiarioId);
        Assert.Equal(99.98m, pagamentoPix.Valor);
        Assert.Equal(TipoChavePix.Email, pagamentoPix.TipoChavePix);
        Assert.Equal("snapshot@exemplo.com", pagamentoPix.ChavePix);
        Assert.Equal(status, pagamentoPix.Status);
        Assert.Equal(quantidadeTentativas, pagamentoPix.QuantidadeTentativas);
        Assert.Equal(createdAt, pagamentoPix.CreatedAt);
        Assert.Equal(updatedAt, pagamentoPix.UpdatedAt);
    }

    [Fact]
    public void Reidratar_QuandoInvariantesOuCombinacaoDeEstadoForemInvalidas_DeveRejeitar()
    {
        var createdAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => Reidratar(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Reidratar(cashbackId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Reidratar(beneficiarioId: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Reidratar(valor: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Reidratar(tipoChavePix: (TipoChavePix)99));
        Assert.Throws<ArgumentException>(() => Reidratar(chavePix: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Reidratar(status: (StatusPagamentoPix)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Reidratar(quantidadeTentativas: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Reidratar(quantidadeTentativas: 6));
        Assert.Throws<ArgumentException>(() => Reidratar(createdAt: DateTime.MinValue));
        Assert.Throws<ArgumentException>(() => Reidratar(createdAt: createdAt, updatedAt: createdAt.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Reidratar(
            status: StatusPagamentoPix.Pendente,
            quantidadeTentativas: 1));
        Assert.Throws<ArgumentException>(() => Reidratar(
            status: StatusPagamentoPix.FalhaDefinitiva,
            quantidadeTentativas: 4));
    }

    private static PagamentoPix Criar(
        Guid? cashbackId = null,
        Guid? beneficiarioId = null,
        decimal valor = 100m,
        TipoChavePix tipoChavePix = TipoChavePix.Email,
        string chavePix = "indicador@exemplo.com") =>
        PagamentoPix.Criar(
            cashbackId ?? Guid.NewGuid(),
            beneficiarioId ?? Guid.NewGuid(),
            valor,
            tipoChavePix,
            chavePix);

    private static PagamentoPix CriarComFalhaDefinitiva()
    {
        var pagamentoPix = Criar();
        for (var tentativa = 0; tentativa < PagamentoPix.TentativasMaximas; tentativa++)
        {
            pagamentoPix.IniciarTentativa();
            pagamentoPix.RegistrarFalha();
        }

        return pagamentoPix;
    }

    private static PagamentoPix Reidratar(
        Guid? id = null,
        Guid? cashbackId = null,
        Guid? beneficiarioId = null,
        decimal valor = 100m,
        TipoChavePix tipoChavePix = TipoChavePix.Email,
        string chavePix = "snapshot@exemplo.com",
        StatusPagamentoPix status = StatusPagamentoPix.Pendente,
        int quantidadeTentativas = 0,
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        var dataCriacao = createdAt ?? new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);
        return PagamentoPix.Reidratar(
            id ?? Guid.NewGuid(),
            cashbackId ?? Guid.NewGuid(),
            beneficiarioId ?? Guid.NewGuid(),
            valor,
            tipoChavePix,
            chavePix,
            status,
            quantidadeTentativas,
            dataCriacao,
            updatedAt ?? dataCriacao);
    }
}

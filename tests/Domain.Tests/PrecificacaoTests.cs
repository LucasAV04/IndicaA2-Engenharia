using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Services;
using Xunit;

namespace Domain.Tests;

public sealed class PrecificacaoTests
{
    private static readonly DateTime Agora = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private static PrecoVistoria Preco(decimal preco = 2.1234m, ModalidadeAcrescimo modo = ModalidadeAcrescimo.Fixo, decimal adicional = 5m) => new(Guid.NewGuid(), "Tipo fictício", preco, modo, adicional, 1, Agora);
    [Theory]
    [InlineData(PacoteVistoria.Simples, ModalidadeAcrescimo.Fixo, "10", "100", "0")]
    [InlineData(PacoteVistoria.Total, ModalidadeAcrescimo.Fixo, "10", "105", "5")]
    [InlineData(PacoteVistoria.Total, ModalidadeAcrescimo.Percentual, "10", "105", "5")]
    public void Formula(PacoteVistoria pacote, ModalidadeAcrescimo modo, string preco, string final, string aplicado)
    {
        var c = MotorPrecificacaoVistoria.Calcular(Preco(decimal.Parse(preco), modo), "Nome atual", 10m, pacote, Agora);
        Assert.Equal(100m, c.ValorBase); Assert.Equal(decimal.Parse(final), c.ValorFinal); Assert.Equal(decimal.Parse(aplicado), c.Acrescimo);
        Assert.Equal("Nome atual", c.NomeTipoPlanta); Assert.Equal(1, c.Versao); Assert.Equal(Agora, c.CalculadoEmUtc);
    }
    [Fact] public void FracionariosEPreservacaoQuatroCasas()
    { var c = MotorPrecificacaoVistoria.Calcular(Preco(), "Tipo", 1.25m, PacoteVistoria.Simples, Agora); Assert.Equal(2.654250m, c.ValorBase); Assert.Equal(2.65m, c.ValorFinal); Assert.Equal(2.1234m, c.PrecoM2); }
    [Fact] public void MeioCentavoAwayFromZero()
    { var c = MotorPrecificacaoVistoria.Calcular(Preco(1.005m), "Tipo", 1m, PacoteVistoria.Simples, Agora); Assert.Equal(1.01m, c.ValorFinal); }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(100000000)][InlineData(0.001)]
    public void AreaInvalida(decimal area) => Assert.Throws<DomainException>(() => MotorPrecificacaoVistoria.Calcular(Preco(), "Tipo", area, PacoteVistoria.Total, Agora));
    [Theory][InlineData(0)][InlineData(-1)][InlineData(100000000)][InlineData(0.00001)]
    public void PrecoInvalido(decimal preco) => Assert.Throws<DomainException>(() => Preco(preco));
    [Fact] public void AcrescimoNegativo() => Assert.Throws<DomainException>(() => Preco(adicional: -1));
    [Fact] public void PercentualForaDoLimite() => Assert.Throws<DomainException>(() => Preco(modo: ModalidadeAcrescimo.Percentual, adicional: 10001));
    [Fact] public void OverflowECapacidadeColuna()
    { Assert.Throws<DomainException>(() => Preco(decimal.MaxValue)); Assert.Throws<DomainException>(() => MotorPrecificacaoVistoria.Calcular(Preco(99999999), "Tipo", 99999999, PacoteVistoria.Total, Agora)); }
    [Fact] public void DeterminismoSemMutacao()
    { var p = Preco(); var a = MotorPrecificacaoVistoria.Calcular(p, "Tipo", 10, PacoteVistoria.Total, Agora); Assert.Equal(a, MotorPrecificacaoVistoria.Calcular(p, "Tipo", 10, PacoteVistoria.Total, Agora)); Assert.True(p.Ativo); Assert.Equal(Agora, p.UpdatedAt); Assert.Equal(5m, p.Acrescimo); }
    [Fact] public void CriarRenomearDesativarTipoPreservaId()
    { var t = new TipoPlanta("  Modelo fictício  ", Agora); var id = t.Id; Assert.Equal("Modelo fictício", t.Nome); Assert.Equal("MODELO FICTÍCIO", t.NomeNormalizado); t.Renomear(" Novo ", Agora.AddMinutes(1)); t.Desativar(Agora.AddMinutes(2)); Assert.Equal(id, t.Id); Assert.Equal("Novo", t.Nome); Assert.False(t.Ativo); Assert.Equal(Agora, t.CreatedAt); }
    [Theory][InlineData("")][InlineData(" ")]
    public void NomeVazio(string nome) => Assert.Throws<DomainException>(() => new TipoPlanta(nome, Agora));
    [Fact] public void NomeLongo() => Assert.Throws<DomainException>(() => new TipoPlanta(new string('x', 151), Agora));
    [Fact] public void SnapshotNaoMudaComPrecoInativo()
    { var p = Preco(); var c = MotorPrecificacaoVistoria.Calcular(p, "Tipo", 10, PacoteVistoria.Total, Agora); var v = Vistoria.CriarCalculada(Guid.NewGuid(), Agora, c); p.Desativar(Agora.AddMinutes(1)); v.Cancelar(); Assert.Same(c, v.Precificacao); Assert.Equal(c.ValorFinal, v.Precificacao!.ValorFinal); }
}

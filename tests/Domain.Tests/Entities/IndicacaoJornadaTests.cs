using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class IndicacaoJornadaTests
{
    [Fact]
    public void CashbackPagoExigeConclusaoERepeticaoNaoAlteraHistorico()
    {
        var indicacao=new Indicacao(Guid.NewGuid(),"Pessoa fictícia","85999990000","ABCD1234");
        Assert.Throws<DomainException>(indicacao.RegistrarCashbackPago);
        indicacao.VincularVistoria(Guid.NewGuid());Assert.Throws<DomainException>(indicacao.RegistrarCashbackPago);
        indicacao.MarcarVistoriaConcluida();indicacao.RegistrarCashbackPago();
        Assert.Equal(StatusIndicacao.CashbackPago,indicacao.Status);
        var updated=indicacao.UpdatedAt;indicacao.RegistrarCashbackPago();Assert.Equal(updated,indicacao.UpdatedAt);
        Assert.Throws<DomainException>(indicacao.Cancelar);
    }
    [Fact]
    public void CanceladaNaoPodeReceberCashbackPago()
    {
        var indicacao=new Indicacao(Guid.NewGuid(),"Pessoa fictícia","85999990000","ABCD1234");
        indicacao.Cancelar();Assert.Throws<DomainException>(indicacao.RegistrarCashbackPago);
    }
    [Fact]
    public void VintePorCentoMantemArredondamentoMonetarioSemNovoTeto()
    {
        var cashback=Cashback.Criar(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),500.05m);
        Assert.Equal(.20m,cashback.Percentual);Assert.Equal(100.01m,cashback.Valor);Assert.Equal(500.05m,cashback.ValorTotalPago);
    }
}

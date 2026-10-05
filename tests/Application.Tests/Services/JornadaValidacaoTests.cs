using Application.Jornada;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Application.Tests.Services;

public sealed class JornadaValidacaoTests
{
    private static IndicacaoPublicaRequest Valida()=>new("abcd1234"," Pessoa Fictícia ","(85) 99999-0000",JornadaValidacao.VersaoTermo,true);

    [Fact]
    public void Normalizar_PreservaEntradaERetornaCamposLimitados()
    {
        var original=Valida(); var normalizado=JornadaValidacao.Normalizar(original);
        Assert.Equal("ABCD1234",normalizado.Codigo);
        Assert.Equal("Pessoa Fictícia",normalizado.Nome);
        Assert.Equal("85999990000",normalizado.Telefone);
        Assert.Equal(" Pessoa Fictícia ",original.Nome);
    }
    [Theory]
    [InlineData("")] [InlineData("1")] [InlineData("85999990000 SQL")]
    [InlineData("１２３４５６７８９０")] [InlineData("9985999990000")]
    public void TelefoneInvalidoFalhaSemEcoarEntrada(string telefone)
    {
        var erro=Assert.Throws<ArgumentException>(()=>JornadaValidacao.Normalizar(Valida() with {Telefone=telefone}));
        Assert.Equal("Telefone inválido.",erro.Message);
    }
    [Theory]
    [InlineData(false,"2026-10-01")] [InlineData(true,"anterior")] [InlineData(true,"")]
    public void ConsentimentoExplicitoEVersaoVigenteSaoObrigatorios(bool aceito,string versao)=>
        Assert.Throws<DomainException>(()=>JornadaValidacao.Normalizar(Valida() with {Consentimento=aceito,VersaoTermo=versao}));

    [Fact]
    public void NomeVazioOuLongoOuControleNaoAceito()
    {
        foreach(var nome in new[]{" ",new string('a',151),"Pessoa\nFicticia"})
            Assert.Throws<ArgumentException>(()=>JornadaValidacao.Normalizar(Valida() with {Nome=nome}));
    }
    [Fact]
    public void ChaveAleatoriaGeraSomenteHashDeterministico()
    {
        var chave=Guid.NewGuid().ToString("D"); var hash=JornadaValidacao.HashChave(chave);
        Assert.Matches("^[0-9a-f]{64}$",hash);
        Assert.Equal(hash,JornadaValidacao.HashChave(chave.ToUpperInvariant()));
        Assert.NotEqual(hash,JornadaValidacao.HashChave(Guid.NewGuid().ToString("D")));
        Assert.DoesNotContain(chave,hash);
        Assert.Throws<ArgumentException>(()=>JornadaValidacao.HashChave(Guid.Empty.ToString()));
    }
    [Fact]
    public void MascarasNaoExibemNomeCompletoOuTelefone()
    {
        Assert.Equal("Pessoa",JornadaValidacao.PrimeiroNome("Pessoa Sobrenome Ficticio"));
        Assert.Equal("••••00",JornadaValidacao.TelefoneMascarado("85999990000"));
    }
    [Theory]
    [InlineData(null)] [InlineData("http://example.invalid")] [InlineData("https://user:secret@example.invalid")]
    [InlineData("https://example.invalid?token=x")] [InlineData("https://example.invalid/#x")]
    public void LinkRecusaBaseInsegura(string? url)=>Assert.Throws<InvalidOperationException>(()=>JornadaValidacao.Link(url,"ABCD1234"));

    [Fact]
    public void LinkUsaSomenteBaseConfigurada()=>Assert.Equal("https://example.invalid/app/indicar/ABCD1234",JornadaValidacao.Link("https://example.invalid/app/","abcd1234"));

    [Fact]
    public void EnumMantemValoresHistoricos()
    {
        Assert.Equal(0,(int)StatusIndicacao.Pendente);
        Assert.Equal(1,(int)StatusIndicacao.VistoriaVinculada);
        Assert.Equal(2,(int)StatusIndicacao.VistoriaConcluida);
        Assert.Equal(3,(int)StatusIndicacao.Cancelada);
        Assert.Equal(4,(int)StatusIndicacao.CashbackPago);
    }
}

using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Xunit;

namespace Domain.Tests;

public sealed class CobrancaPixVistoriaTests
{
    private static readonly DateTime Agora=new(2026,9,25,12,0,0,DateTimeKind.Utc);
    [Fact] public void IdentidadeEstavelSemPiiEValorImutavel()
    {
        var id=Guid.NewGuid(); var c=new CobrancaPixVistoria(id,12.35m,3600,Agora);
        Assert.Equal(c.Id.ToString("N"),c.Txid); Assert.Matches("^[a-zA-Z0-9]{32}$",c.Txid);
        Assert.Equal(id,c.PagamentoVistoriaId); Assert.Equal(12.35m,c.Valor); Assert.Equal(Agora,c.CreatedAt);
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(1.001)] [InlineData(10000000000)]
    public void ValorInvalido(decimal valor) => Assert.Throws<DomainException>(()=>new CobrancaPixVistoria(Guid.NewGuid(),valor,3600,Agora));
    [Theory] [InlineData(0)] [InlineData(59)] [InlineData(86401)]
    public void PrazoInvalido(int prazo) => Assert.Throws<DomainException>(()=>new CobrancaPixVistoria(Guid.NewGuid(),1,prazo,Agora));
    [Theory] [InlineData(StatusCobrancaPixVistoria.Expirada,true)] [InlineData(StatusCobrancaPixVistoria.Removida,true)]
    [InlineData(StatusCobrancaPixVistoria.Ativa,false)] [InlineData(StatusCobrancaPixVistoria.Indeterminada,false)]
    [InlineData(StatusCobrancaPixVistoria.FalhaDefinitiva,true)]
    [InlineData(StatusCobrancaPixVistoria.Confirmada,false)] [InlineData(StatusCobrancaPixVistoria.DivergenciaFinanceira,false)]
    public void ReemissaoExigeTerminalComprovado(StatusCobrancaPixVistoria status,bool permitido) => Assert.Equal(permitido,CobrancaPixVistoria.PermiteReemissao(status));
    [Fact] public void ConfirmacaoUsaHorarioProviderEEvidenciaIdempotente()
    {
        var p=new PagamentoVistoria(Guid.NewGuid(),10); var evidencia=Guid.NewGuid();
        p.ConfirmarRecebimento(evidencia,Agora,Agora.AddSeconds(1));
        p.ConfirmarRecebimento(evidencia,Agora,Agora.AddSeconds(2));
        Assert.Equal(Agora,p.PagoEm); Assert.Equal(Agora.AddSeconds(1),p.UpdatedAt);
        Assert.Throws<DomainException>(()=>p.ConfirmarRecebimento(Guid.NewGuid(),Agora,Agora.AddSeconds(3)));
    }
}

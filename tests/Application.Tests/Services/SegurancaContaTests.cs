using Application.Contas;
using Xunit;

namespace Application.Tests.Services;

public sealed class SegurancaContaTests
{
    [Fact]
    public void TokensAleatoriosTem256BitsEHashDeterministico()
    {
        var token = SegurancaConta.GerarToken();
        Assert.Equal(64, token.Length);
        Assert.NotEqual(token, SegurancaConta.GerarToken());
        var hash = Assert.IsType<byte[]>(SegurancaConta.HashToken(token));
        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, SegurancaConta.HashToken(token));
        Assert.Null(SegurancaConta.HashToken(token + " "));
    }

    [Fact]
    public void FinalidadeDeterminaValidade()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), SegurancaConta.Validade(FinalidadeTokenConta.RedefinicaoSenha));
        Assert.Equal(TimeSpan.FromHours(24), SegurancaConta.Validade(FinalidadeTokenConta.ConfirmacaoEmail));
        Assert.Throws<ArgumentOutOfRangeException>(() => SegurancaConta.Validade((FinalidadeTokenConta)255));
    }

    [Fact]
    public void PoliticaRespeitaLimiteEmBytesDoBCrypt()
    {
        Assert.False(SegurancaConta.NovaSenhaValida("curta"));
        Assert.False(SegurancaConta.NovaSenhaValida(new string(' ', 8)));
        Assert.True(SegurancaConta.NovaSenhaValida(new string('é', 36)));
        Assert.False(SegurancaConta.NovaSenhaValida(new string('é', 37)));
    }
}

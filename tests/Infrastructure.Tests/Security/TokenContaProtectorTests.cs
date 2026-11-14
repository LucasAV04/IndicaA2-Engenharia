using System.Security.Cryptography;
using Application.Contas;
using Infrastructure.Security;
using Xunit;

namespace Infrastructure.Tests.Security;

public sealed class TokenContaProtectorTests
{
    [Fact]
    public void CifraVinculaUsuarioReferenciaEFinalidade()
    {
        var protector = new TokenContaProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var token = SegurancaConta.GerarToken();
        var usuario = Guid.NewGuid();
        var referencia = Guid.NewGuid();
        const FinalidadeTokenConta finalidade = FinalidadeTokenConta.RedefinicaoSenha;
        var material = protector.Cifrar(token, usuario, referencia, finalidade);

        Assert.Equal(token, protector.Decifrar(material, usuario, referencia, finalidade));
        Assert.NotEqual(material.Nonce, protector.Cifrar(token, usuario, referencia, finalidade).Nonce);
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            protector.Decifrar(material, Guid.NewGuid(), referencia, finalidade));
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            protector.Decifrar(material, usuario, Guid.NewGuid(), finalidade));
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            protector.Decifrar(material, usuario, referencia, FinalidadeTokenConta.ConfirmacaoEmail));
    }
}

using Infrastructure.Security;
using System.Security.Cryptography;
using Xunit;

namespace Infrastructure.Tests.Security;

public sealed class CobrancaPixProtectorTests
{
    [Fact] public void CodigoCifradoNonceUnicoEContextoAutenticado()
    {
        using var p=new CobrancaPixProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var id=Guid.NewGuid(); const string codigo="CODIGO_FICTICIO_SECRETO";
        var a=p.Proteger(id,codigo); var b=p.Proteger(id,codigo);
        Assert.NotEqual(a.Nonce,b.Nonce); Assert.Equal(codigo,p.Desproteger(id,a));
        Assert.DoesNotContain(codigo,System.Text.Encoding.UTF8.GetString(a.Ciphertext));
        Assert.Throws<CryptographicException>(()=>p.Desproteger(Guid.NewGuid(),a));
        a.Ciphertext[0]^=1; Assert.Throws<CryptographicException>(()=>p.Desproteger(id,a));
    }
    [Theory] [InlineData("")] [InlineData("não-base64")] [InlineData("YQ==")]
    public void ChaveInvalidaFalhaFechado(string key) => Assert.ThrowsAny<ArgumentException>(()=>new CobrancaPixProtector(key));
}

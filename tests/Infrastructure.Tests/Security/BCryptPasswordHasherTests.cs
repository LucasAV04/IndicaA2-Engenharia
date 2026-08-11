using Infrastructure.Security;
using Xunit;

namespace Infrastructure.Tests.Security;

public sealed class BCryptPasswordHasherTests
{
    [Fact]
    public void HashPassword_DeveGerarHashQueValidaSenhaCorretaENaoValidaIncorreta()
    {
        var hasher = new BCryptPasswordHasher();
        var hash = hasher.HashPassword("SenhaSegura123!");
        Assert.NotEqual("SenhaSegura123!", hash);
        Assert.True(hasher.VerifyPassword("SenhaSegura123!", hash));
        Assert.False(hasher.VerifyPassword("SenhaIncorreta", hash));
    }

    [Fact]
    public void HashPassword_QuandoSenhaForInvalida_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new BCryptPasswordHasher().HashPassword(" "));
    }
}

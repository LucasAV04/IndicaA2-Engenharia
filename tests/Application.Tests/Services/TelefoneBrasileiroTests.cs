using Application.Jornada;
using Xunit;

namespace Application.Tests.Services;

public sealed class TelefoneBrasileiroTests
{
    [Theory]
    [InlineData("(79) 99999-9999", "79999999999")]
    [InlineData("5579999999999", "5579999999999")]
    [InlineData("(55) 79 99999-9999", "5579999999999")]
    [InlineData("(55) (79) 3333-4444", "557933334444")]
    [InlineData("55999999999", "55999999999")]
    [InlineData("7933334444", "7933334444")]
    [InlineData("+55 (79) 3333-4444", "557933334444")]
    public void AceitaFormatoBrasileiro(string entrada, string canonico)
    {
        Assert.Equal(canonico, TelefoneBrasileiro.Normalizar(entrada));
    }

    [Theory]
    [InlineData("00000000000")]
    [InlineData("5500999999999")]
    [InlineData("55")]
    [InlineData("")]
    [InlineData("+1 212 555 0100")]
    [InlineData("79999999")]
    [InlineData("7999999999999")]
    [InlineData("(55) 99999-99990")]
    public void RejeitaIncompletoOuEstrangeiro(string entrada)
    {
        Assert.Throws<ArgumentException>(() => TelefoneBrasileiro.Normalizar(entrada));
    }

    [Fact]
    public void AceitaTelefoneOpcionalVazio()
    {
        Assert.Null(TelefoneBrasileiro.Normalizar(" ", opcional: true));
    }
}

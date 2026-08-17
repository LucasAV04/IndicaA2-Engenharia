using Infrastructure.Security;
using Xunit;

namespace Infrastructure.Tests.Security;

public sealed class CodigoIndicacaoGeneratorTests
{
    [Fact]
    public void Gerar_DeveProduzirCodigosComOFormatoOficial()
    {
        var generator = new CodigoIndicacaoGenerator();

        var codigos = Enumerable.Range(0, 20).Select(_ => generator.Gerar()).ToList();

        Assert.All(codigos, codigo =>
        {
            Assert.Equal(8, codigo.Length);
            Assert.All(codigo, caractere => Assert.True(char.IsAsciiLetterUpper(caractere) || char.IsAsciiDigit(caractere)));
        });
    }
}

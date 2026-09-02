using Domain.Entities;
using Xunit;

namespace Infrastructure.Tests.Providers;

public sealed class OperacaoPagamentoPixSecurityTests
{
    [Fact]
    public void ModeloDeAuditoria_NaoDeveExporCamposSensiveisDePix()
    {
        var propriedades = typeof(OperacaoPagamentoPix).GetProperties().Select(propriedade => propriedade.Name).ToArray();

        Assert.DoesNotContain(propriedades, nome => nome.Contains("ChavePix", StringComparison.Ordinal));
        Assert.DoesNotContain(propriedades, nome => nome.Contains("Token", StringComparison.Ordinal));
        Assert.DoesNotContain(propriedades, nome => nome.Contains("Certificado", StringComparison.Ordinal));
        Assert.DoesNotContain(propriedades, nome => nome.Contains("Payload", StringComparison.Ordinal));
    }
}

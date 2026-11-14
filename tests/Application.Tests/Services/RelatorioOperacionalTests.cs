using Application.Relatorios;
using Xunit;

namespace Application.Tests.Services;

public sealed class RelatorioOperacionalTests
{
    [Theory]
    [InlineData("=1", "\"'=1\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-1", "\"'-1\"")]
    [InlineData(" @valor", "\"' @valor\"")]
    [InlineData("\r\n\t=1", "\"'=1\"")]
    [InlineData("texto\"normal", "\"texto\"\"normal\"")]
    public void CsvNeutralizaFormulasEControles(string entrada, string esperado)
        => Assert.Equal(esperado, CsvRelatorio.Celula(entrada));

    [Fact]
    public void RelatorioLimitaPeriodoEPaginacao()
    {
        var inicio = new DateOnly(2025, 1, 1);
        var filtro = new FiltroRelatorio(TipoRelatorio.Emails, inicio, inicio.AddDays(365), null, 2, 100);
        Assert.True(filtro.Valido);
        Assert.Equal(100L, filtro.Deslocamento);
        Assert.Equal(DateTimeKind.Utc, filtro.DeUtc.Kind);
        Assert.False((filtro with { Fim = inicio.AddDays(366) }).Valido);
        Assert.False((filtro with { Pagina = 0 }).Valido);
        Assert.False((filtro with { Tamanho = 101 }).Valido);
    }
}

using Application.Interfaces.Providers;
using Domain.Enums;
using System.Globalization;
using Xunit;

namespace Application.Tests.Providers;

public sealed class PixProviderContractTests
{
    [Fact]
    public void PixEnvioRequest_ParaMesmaOrdem_DeveGerarReferenciaIdempotenteEstavel()
    {
        var pagamentoPixId = Guid.Parse("94fd293e-8ed9-4672-9a07-63d4f1891c4d");

        var primeira = CriarEnvioRequest(pagamentoPixId);
        var segunda = CriarEnvioRequest(pagamentoPixId);

        Assert.Equal("94fd293e8ed946729a0763d4f1891c4d", primeira.ReferenciaIdempotente);
        Assert.Equal(primeira.ReferenciaIdempotente, segunda.ReferenciaIdempotente);
        Assert.Equal(32, primeira.ReferenciaIdempotente.Length);
    }

    [Fact]
    public void PixEnvioRequest_ParaOrdensDiferentes_DeveGerarReferenciasDiferentes()
    {
        var primeira = CriarEnvioRequest(Guid.NewGuid());
        var segunda = CriarEnvioRequest(Guid.NewGuid());

        Assert.NotEqual(primeira.ReferenciaIdempotente, segunda.ReferenciaIdempotente);
    }

    [Fact]
    public void PixReferenciaIdempotente_DeveSerIndependenteDeCulturaEHorario()
    {
        var pagamentoPixId = Guid.Parse("94fd293e-8ed9-4672-9a07-63d4f1891c4d");
        var culturaOriginal = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var referenciaEmPortugues = PixReferenciaIdempotente.Criar(pagamentoPixId);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var referenciaEmIngles = PixReferenciaIdempotente.Criar(pagamentoPixId);

            Assert.Equal(referenciaEmPortugues, referenciaEmIngles);
            Assert.Equal(pagamentoPixId.ToString("N"), referenciaEmIngles);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
        }
    }

    [Fact]
    public void PixConsultaRequest_DeveUsarAMesmaReferenciaDaOrdemParaReconciliacao()
    {
        var pagamentoPixId = Guid.NewGuid();

        var consulta = new PixConsultaRequest(pagamentoPixId);
        var envio = CriarEnvioRequest(pagamentoPixId);

        Assert.Equal(envio.ReferenciaIdempotente, consulta.ReferenciaIdempotente);
    }

    [Fact]
    public void PixEnvioRequest_NaoDeveExporChavePixNoToString()
    {
        const string chavePix = "indicador-ficticio@exemplo.com";

        var request = new PixEnvioRequest(
            Guid.NewGuid(),
            100m,
            TipoChavePix.Email,
            chavePix);

        Assert.DoesNotContain(chavePix, request.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StatusPixProvider.Confirmado, true, false, false)]
    [InlineData(StatusPixProvider.FalhaConfirmada, false, true, false)]
    [InlineData(StatusPixProvider.Pendente, false, false, true)]
    [InlineData(StatusPixProvider.Indeterminado, false, false, true)]
    public void PixProviderResult_DeveDistinguirSemanticaDosResultados(
        StatusPixProvider status,
        bool ehConfirmado,
        bool ehFalhaConfirmada,
        bool requerReconciliacao)
    {
        var resultado = status switch
        {
            StatusPixProvider.Confirmado => PixProviderResult.Confirmado("provider-id", "codigo"),
            StatusPixProvider.FalhaConfirmada => PixProviderResult.FalhaConfirmada("provider-id", "codigo"),
            StatusPixProvider.Pendente => PixProviderResult.Pendente("provider-id", "codigo"),
            StatusPixProvider.Indeterminado => PixProviderResult.Indeterminado("provider-id", "codigo"),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        Assert.Equal(status, resultado.Status);
        Assert.Equal(ehConfirmado, resultado.EhConfirmado);
        Assert.Equal(ehFalhaConfirmada, resultado.EhFalhaConfirmada);
        Assert.Equal(requerReconciliacao, resultado.RequerReconciliacao);
        Assert.Equal("provider-id", resultado.IdentificadorProvider);
        Assert.Equal("codigo", resultado.Codigo);
    }

    [Fact]
    public void IPixProvider_DeveExporEnvioEConsultaComCancellationTokenESemTiposDaEfi()
    {
        var metodos = typeof(IPixProvider).GetMethods();

        Assert.Collection(
            metodos.OrderBy(method => method.Name),
            consultar =>
            {
                Assert.Equal(nameof(IPixProvider.ConsultarAsync), consultar.Name);
                Assert.Equal(typeof(Task<PixProviderResult>), consultar.ReturnType);
                Assert.Equal(typeof(PixConsultaRequest), consultar.GetParameters()[0].ParameterType);
                Assert.Equal(typeof(CancellationToken), consultar.GetParameters()[1].ParameterType);
            },
            enviar =>
            {
                Assert.Equal(nameof(IPixProvider.EnviarAsync), enviar.Name);
                Assert.Equal(typeof(Task<PixProviderResult>), enviar.ReturnType);
                Assert.Equal(typeof(PixEnvioRequest), enviar.GetParameters()[0].ParameterType);
                Assert.Equal(typeof(CancellationToken), enviar.GetParameters()[1].ParameterType);
            });

        Assert.DoesNotContain(
            typeof(IPixProvider).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.Contains("Efi", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void PixProviderResult_NaoDeveConterChavePix()
    {
        Assert.Null(typeof(PixProviderResult).GetProperty("ChavePix"));
        Assert.Null(typeof(PixProviderResult).GetProperty("EndToEndId"));
    }

    private static PixEnvioRequest CriarEnvioRequest(Guid pagamentoPixId) =>
        new(pagamentoPixId, 100m, TipoChavePix.Email, "indicador-ficticio@exemplo.com");
}

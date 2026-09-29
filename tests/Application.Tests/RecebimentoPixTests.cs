using Application.Recebimentos;
using Domain.Enums;
using Moq;
using Xunit;

namespace Application.Tests;

public sealed class RecebimentoPixTests
{
    [Fact]
    public void IdentidadeDoEventoPreservaPrecisaoMySqlSemDivergenciaArtificial()
    {
        var horario = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        var original = new EventoPix("E00000000202609251200ABCDEFGHIJK", new string('a',32),12.34m,horario);
        var persistido = RecebimentoPixValidacao.Canonicalizar(original);
        Assert.Equal(0,persistido.Horario.Ticks % 10);
        Assert.Equal(DateTimeKind.Utc,persistido.Horario.Kind);
        Assert.Equal(horario,original.Horario);
        Assert.Equal(RecebimentoPixValidacao.HashEvento(original),RecebimentoPixValidacao.HashEvento(persistido));
        Assert.NotEqual(RecebimentoPixValidacao.HashEvento(original),RecebimentoPixValidacao.HashEvento(original with { Valor=12.35m }));
    }
    private readonly RecebimentoPixOptions options=new() { Habilitado=true,UrlPublica="https://example.invalid" };
    [Theory] [InlineData(OperacaoCobranca.Criar)] [InlineData(OperacaoCobranca.Consultar)] [InlineData(OperacaoCobranca.Remover)]
    public async Task OperacaoUnicaForaDoStoreEFinalizacaoNaoCancelavel(OperacaoCobranca operacao)
    {
        var store=new Mock<ICobrancaPixVistoriaStore>(); var provider=new Mock<ICobrancaPixVistoriaProvider>();
        var p=new PreparacaoCobranca(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid().ToString("N"),12.34m,3600,Guid.NewGuid(),Guid.NewGuid(),operacao);
        using var cts=new CancellationTokenSource(); var resposta=new ResultadoCobrancaProvider(SituacaoCobrancaProvider.Indeterminada,"timeout");
        store.Setup(x=>x.AdquirirAsync(p.Id,cts.Token)).ReturnsAsync(p);
        provider.Setup(x=>x.CriarAsync(p.Txid,p.Valor,p.ExpiracaoSegundos,cts.Token)).ReturnsAsync(()=>{cts.Cancel();return resposta;});
        provider.Setup(x=>x.ConsultarAsync(p.Txid,cts.Token)).ReturnsAsync(()=>{cts.Cancel();return resposta;});
        provider.Setup(x=>x.RemoverAsync(p.Txid,cts.Token)).ReturnsAsync(()=>{cts.Cancel();return resposta;});
        await new RecebimentoPixProcessamentoService(store.Object,Mock.Of<IRecebimentoPixWebhookStore>(),provider.Object,options).ProcessarCobrancaAsync(p.Id,cts.Token);
        Assert.Single(provider.Invocations);
        store.Verify(x=>x.FinalizarAsync(p,resposta,CancellationToken.None),Times.Once);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ExcecaoOuCancelamentoRegistraIndeterminadoSemRetry(bool cancelar)
    {
        var store=new Mock<ICobrancaPixVistoriaStore>(); var provider=new Mock<ICobrancaPixVistoriaProvider>();
        var p=new PreparacaoCobranca(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid().ToString("N"),1,3600,Guid.NewGuid(),Guid.NewGuid(),OperacaoCobranca.Criar);
        store.Setup(x=>x.AdquirirAsync(p.Id,default)).ReturnsAsync(p);
        provider.Setup(x=>x.CriarAsync(p.Txid,1,3600,default)).ThrowsAsync(cancelar?new OperationCanceledException():new HttpRequestException("SEGREDO_FICTICIO"));
        await Assert.ThrowsAnyAsync<Exception>(()=>new RecebimentoPixProcessamentoService(store.Object,Mock.Of<IRecebimentoPixWebhookStore>(),provider.Object,options).ProcessarCobrancaAsync(p.Id,default));
        store.Verify(x=>x.FinalizarAsync(p,It.Is<ResultadoCobrancaProvider>(r=>r.Situacao==SituacaoCobrancaProvider.Indeterminada),CancellationToken.None),Times.Once);
        Assert.Single(provider.Invocations);
    }
    [Fact] public async Task LeaseNaoAdquiridoNaoChamaProvider()
    {
        var provider=new Mock<ICobrancaPixVistoriaProvider>();
        await new RecebimentoPixProcessamentoService(Mock.Of<ICobrancaPixVistoriaStore>(),Mock.Of<IRecebimentoPixWebhookStore>(),provider.Object,options).ProcessarCobrancaAsync(Guid.NewGuid(),default);
        Assert.Empty(provider.Invocations);
    }
    [Fact] public async Task LinkSoPersisteHashENaoExpoeEmToString()
    {
        var store=new Mock<ICobrancaPixVistoriaStore>(); var id=Guid.NewGuid(); byte[]? hash=null;
        store.Setup(x=>x.RotacionarLinkAsync(id,It.IsAny<byte[]>(),It.IsAny<DateTime>(),default)).Callback<Guid,byte[],DateTime,CancellationToken>((_,h,_,_)=>hash=h).Returns(Task.CompletedTask);
        var service=new CobrancaPixVistoriaService(store.Object,Mock.Of<IRecebimentoPixProcessamentoService>(),options,TimeProvider.System);
        var link=await service.RotacionarLinkAsync(id,default); var token=link.Link.Split('#')[1];
        Assert.Equal(64,token.Length); Assert.Equal(RecebimentoPixValidacao.HashLink(token),hash); Assert.DoesNotContain(token,link.ToString());
        var segundo=await service.RotacionarLinkAsync(id,default); Assert.NotEqual(link.Link,segundo.Link);
    }
    [Theory] [InlineData("")] [InlineData("abc")] [InlineData("?token=segredo")]
    public async Task LinkInvalidoNaoAcessaPersistencia(string token)
    {
        var store=new Mock<ICobrancaPixVistoriaStore>();
        Assert.Null(await new CobrancaPixVistoriaService(store.Object,Mock.Of<IRecebimentoPixProcessamentoService>(),options,TimeProvider.System).ObterPublicaAsync(token,default));
        Assert.Empty(store.Invocations);
    }
    [Fact] public void DesabilitadoNaoExigeSegredos() => new RecebimentoPixOptions().Validar();
    [Fact] public void HabilitadoSemSegredosFalha() => Assert.Throws<InvalidOperationException>(()=>new RecebimentoPixOptions { Habilitado=true }.Validar());
    [Fact] public void DtosPublicosNaoExibemIdentidadeInterna()
    {
        Assert.Equal(new[]{"Valor","Status","VenceEm","PixCopiaECola","Confirmada"},typeof(CobrancaPublica).GetProperties().Select(p=>p.Name));
        Assert.DoesNotContain("segredo",new CobrancaPublica(1,StatusCobrancaPixVistoria.Ativa,null,"segredo",false).ToString());
    }
}

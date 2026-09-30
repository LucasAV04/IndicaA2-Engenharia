using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using API.Controllers;
using API.Security;
using Application.Recebimentos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace API.Tests;

public sealed class RecebimentoPixWebhookTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task SemCertificadoTlsNaoLeCorpoMesmoComHeaderForjado(bool https, bool header)
    {
        var context = Context(); context.Request.Scheme = https ? "https" : "http";
        context.Request.Body = new CorpoProibido();
        if (header) context.Request.Headers["X-Client-Cert"] = "certificado-forjado";
        var chamado = false;
        await new RecebimentoPixMtlsMiddleware(_ => { chamado = true; return Task.CompletedTask; })
            .InvokeAsync(context, new RecebimentoPixOptions { Habilitado = true });
        Assert.Equal(403, context.Response.StatusCode); Assert.False(chamado);
    }

    [Fact]
    public async Task DesabilitadoNaoExigeCertificadoENaoLeCorpo()
    {
        var context = Context(); context.Request.Body = new CorpoProibido();
        await new RecebimentoPixMtlsMiddleware(_ => throw new InvalidOperationException())
            .InvokeAsync(context, new RecebimentoPixOptions());
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void CadeiaClienteExigeAutoridadeConfiavelEValidade(bool autoridadeDiferente, bool expirado, bool esperado)
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=CA Ficticia Teste", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(30));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=Cliente Ficticio", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        using var leaf = leafRequest.Create(ca, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(expirado ? -1 : 1), RandomNumberGenerator.GetBytes(16));
        using var otherKey = RSA.Create(2048);
        var otherRequest = new CertificateRequest("CN=Outra CA", otherKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        otherRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var other = otherRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(Path.GetTempPath(), $"recebimento-ca-{Guid.NewGuid():N}.pem");
        try
        {
            File.WriteAllText(path, (autoridadeDiferente ? other : ca).ExportCertificatePem());
            Assert.Equal(esperado, RecebimentoPixMtlsMiddleware.Confiavel(leaf, path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"pix\":[]}")]
    [InlineData("{\"pix\":[{\"horario\":null}]}")]
    [InlineData("{\"pix\":[{\"horario\":7}]}")]
    [InlineData("{\"pix\":false}")]
    public async Task PayloadInvalidoNaoPersiste(string json)
    {
        var store = new Mock<IRecebimentoPixWebhookStore>();
        var controller = Controller(store.Object, json);
        Assert.IsType<BadRequestResult>(await controller.Receber(default));
        Assert.Empty(store.Invocations);
    }

    [Fact]
    public async Task BodyGrandeEhRecusadoAntesDaPersistencia()
    {
        var store = new Mock<IRecebimentoPixWebhookStore>();
        var result = Assert.IsType<StatusCodeResult>(await Controller(store.Object,new string('x',65537)).Receber(default));
        Assert.Equal(413,result.StatusCode); Assert.Empty(store.Invocations);
    }

    [Fact]
    public async Task SucessoSomenteDepoisDaPersistenciaSemDadosPessoaisNoContrato()
    {
        var store = new Mock<IRecebimentoPixWebhookStore>();
        var sinal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Setup(x=>x.PersistirAsync(It.IsAny<IReadOnlyList<EventoPix>>(),default)).Returns(async()=>{ sinal.SetResult(); await liberar.Task; });
        var tarefa = Controller(store.Object,EventoJson()).Receber(default);
        try
        {
            await sinal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(tarefa.IsCompleted);
        }
        finally { liberar.TrySetResult(); await tarefa.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.IsType<OkResult>(await tarefa);
        var eventos = Assert.IsAssignableFrom<IReadOnlyList<EventoPix>>(Assert.Single(store.Invocations).Arguments[0]);
        Assert.DoesNotContain("SEGREDO_FICTICIO",Assert.Single(eventos).ToString());
    }

    [Fact]
    public async Task FalhaDePersistenciaNaoEhMascaradaComoPayloadInvalidoOuSucesso()
    {
        var store = new Mock<IRecebimentoPixWebhookStore>();
        store.Setup(x=>x.PersistirAsync(It.IsAny<IReadOnlyList<EventoPix>>(),default)).ThrowsAsync(new InvalidOperationException("falha-controlada"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Controller(store.Object,EventoJson()).Receber(default));
    }

    [Theory]
    [InlineData("endToEndId","invalido")]
    [InlineData("txid","invalido")]
    [InlineData("valor","-1")]
    [InlineData("valor","1.001")]
    [InlineData("horario","sem-data")]
    [InlineData("horario","2036-09-25T12:00:00Z")]
    public async Task CamposInvalidosNaoChegamAoStore(string campo,string valor)
    {
        var json=System.Text.Json.Nodes.JsonNode.Parse(EventoJson())!;
        json["pix"]![0]![campo]=valor;
        var store=new Mock<IRecebimentoPixWebhookStore>(MockBehavior.Strict);
        Assert.IsType<BadRequestResult>(await Controller(store.Object,json.ToJsonString()).Receber(default));
        store.VerifyNoOtherCalls();
    }

    [Theory] [InlineData(2,true)] [InlineData(101,false)]
    public async Task LoteRespeitaLimiteESomenteCamposMinimos(int quantidade,bool valido)
    {
        var item=System.Text.Json.Nodes.JsonNode.Parse(EventoJson())!["pix"]![0]!;
        var itens=new System.Text.Json.Nodes.JsonArray();
        for(var i=0;i<quantidade;i++) itens.Add(item.DeepClone());
        var json=new System.Text.Json.Nodes.JsonObject { ["pix"]=itens };
        var store=new Mock<IRecebimentoPixWebhookStore>();
        var result=await Controller(store.Object,json.ToJsonString()).Receber(default);
        if(valido)
        {
            Assert.IsType<OkResult>(result);
            var eventos=Assert.IsAssignableFrom<IReadOnlyList<EventoPix>>(Assert.Single(store.Invocations).Arguments[0]);
            Assert.Equal(quantidade,eventos.Count);
            Assert.DoesNotContain("SEGREDO_FICTICIO",System.Text.Json.JsonSerializer.Serialize(eventos));
        }
        else { Assert.IsType<BadRequestResult>(result); store.VerifyNoOtherCalls(); }
    }

    private static string EventoJson() => """{"pix":[{"endToEndId":"E00000000202609251200ABCDEFGHIJK","txid":"11111111111111111111111111111111","valor":"12.34","horario":"2026-09-25T12:00:00Z","pagador":{"nome":"SEGREDO_FICTICIO"}}]}""";
    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext(); context.Request.Path = "/api/webhooks/efi/pix"; return context;
    }
    private static RecebimentoPixWebhookController Controller(IRecebimentoPixWebhookStore store,string body)
    {
        var context = Context(); context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new(store,new Clock()) { ControllerContext = new ControllerContext { HttpContext=context } };
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026,9,25,12,0,0,TimeSpan.Zero); }
    private sealed class CorpoProibido : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default) => throw new InvalidOperationException("Corpo não pode ser lido.");
    }
}

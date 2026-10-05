using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Application.Jornada;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace API.Tests.Integration;

public sealed class PortalJornadaPipelineTests(ApiTestWebApplicationFactory factory):IClassFixture<ApiTestWebApplicationFactory>
{
    private static string Token(Guid id,string role="Usuario")=>new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "IndicA2.Api.Tests","IndicA2.Api.Tests.Client",[new Claim("sub",id.ToString()),new Claim("role",role)],
        expires:DateTime.UtcNow.AddMinutes(5),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("chave-ficticia-de-autorizacao-com-mais-de-trinta-e-dois-bytes")),SecurityAlgorithms.HmacSha256)));
    [Theory]
    [InlineData("/api/minha-conta/indicacoes")][InlineData("/api/minha-conta/cashbacks")][InlineData("/api/notificacoes")]
    public async Task AnonimoNaoAcessaPortal(string path)
    {
        using var client=factory.CreateHttpsClient();using var resposta=await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized,resposta.StatusCode);
    }
    [Theory]
    [InlineData("/api/admin/jornada/indicadores")][InlineData("/api/admin/jornada/notificacoes")][InlineData("/api/admin/dashboard")]
    public async Task UsuarioComumNaoAcessaEscopoAdministrativo(string path)
    {
        using var client=factory.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token(Guid.NewGuid()));
        using var resposta=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,resposta.StatusCode);
    }
    [Fact]
    public async Task ListagensUsamSomenteSubERejeitamIdentidadeNaQuery()
    {
        var usuario=Guid.NewGuid();var outro=Guid.NewGuid();var store=new Mock<IJornadaConsultaStore>(MockBehavior.Strict);
        store.Setup(x=>x.IndicacoesAsync(usuario,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndicacaoPortal>());
        store.Setup(x=>x.CashbacksAsync(usuario,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CashbackPortal>());
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddScoped(_=>store.Object)));
        using var client=app.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token(usuario));
        foreach(var caminho in new[]{"indicacoes","cashbacks"})
        {
            using var r=await client.GetAsync($"/api/minha-conta/{caminho}");Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal("[]",await r.Content.ReadAsStringAsync());
            using var troca=await client.GetAsync($"/api/minha-conta/{caminho}?usuarioId={outro}");Assert.Equal(HttpStatusCode.NotFound,troca.StatusCode);
        }
        store.VerifyAll();
        using var arbitrario=await client.GetAsync($"/api/minha-conta/{outro}/cashbacks");Assert.Equal(HttpStatusCode.NotFound,arbitrario.StatusCode);
    }
    [Fact]
    public async Task MarcarNotificacaoDeOutroUsuarioRetorna404SemConfirmarExistencia()
    {
        var usuario=Guid.NewGuid();var notificacao=Guid.NewGuid();var store=new Mock<IJornadaConsultaStore>(MockBehavior.Strict);
        store.Setup(x=>x.LerAsync(usuario,false,notificacao,It.IsAny<DateTime>(),It.IsAny<CancellationToken>())).ReturnsAsync(false);
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddScoped(_=>store.Object)));
        using var client=app.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token(usuario));
        using var r=await client.PatchAsync($"/api/notificacoes/{notificacao}/lida",null);Assert.Equal(HttpStatusCode.NotFound,r.StatusCode);store.VerifyAll();
    }
    [Fact]
    public async Task DadosPixNaoAceitamDestinatarioNoPayload()
    {
        var store=new Mock<IJornadaConsultaStore>(MockBehavior.Strict);var dados=new Mock<IDadosPixService>(MockBehavior.Strict);
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{s.AddScoped(_=>store.Object);s.AddScoped(_=>dados.Object);}));
        using var client=app.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token(Guid.NewGuid()));
        using var r=await client.PutAsJsonAsync("/api/minha-conta/dados-pix",new{tipoChavePix=2,chavePix="ficticio@example.invalid",usuarioId=Guid.NewGuid()});
        Assert.Equal(HttpStatusCode.NotFound,r.StatusCode);store.VerifyNoOtherCalls();dados.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task PerfilNaoConfiaNoHostParaLinkCompartilhado()
    {
        var id=Guid.NewGuid();var store=new Mock<IJornadaConsultaStore>(MockBehavior.Strict);
        store.Setup(x=>x.PerfilAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(("Pessoa fictícia","ABCD1234"));
        using var app=factory.WithWebHostBuilder(b=>
        {
            b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["PublicWeb:BaseUrl"]="https://example.invalid"}));
            b.ConfigureTestServices(s=>s.AddScoped(_=>store.Object));
        });
        using var client=app.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token(id));client.DefaultRequestHeaders.Host="host-nao-confiavel.invalid";
        using var r=await client.GetAsync("/api/minha-conta");Assert.Equal(HttpStatusCode.OK,r.StatusCode);
        var json=await r.Content.ReadAsStringAsync();Assert.Contains("https://example.invalid/indicar/ABCD1234",json);Assert.DoesNotContain("host-nao-confiavel",json);
    }
    [Fact]
    public async Task DadosPixRetornamSomenteMascaraETipoSemIdentificadores()
    {
        var id=Guid.NewGuid();var store=new Mock<IJornadaConsultaStore>(MockBehavior.Strict);var dados=new Mock<IDadosPixService>(MockBehavior.Strict);
        dados.Setup(x=>x.ObterPorUsuarioIdAsync(id,It.IsAny<CancellationToken>())).ReturnsAsync(new Application.DTOs.DadosPix.DadosPixResponseDto{Id=Guid.NewGuid(),UsuarioId=id,TipoChavePix=Domain.Enums.TipoChavePix.Email,ChavePix="segredo-ficticio@example.invalid"});
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{s.AddScoped(_=>store.Object);s.AddScoped(_=>dados.Object);}));
        using var client=app.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token(id));
        using var r=await client.GetAsync("/api/minha-conta/dados-pix");Assert.Equal(HttpStatusCode.OK,r.StatusCode);
        var json=await r.Content.ReadAsStringAsync();Assert.DoesNotContain("segredo-ficticio",json);Assert.DoesNotContain(id.ToString(),json);
        using var doc=System.Text.Json.JsonDocument.Parse(json);Assert.Equal(new[]{"tipoChavePix","chaveMascarada"},doc.RootElement.EnumerateObject().Select(p=>p.Name));
    }
}

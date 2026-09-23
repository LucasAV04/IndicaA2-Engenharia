using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Application.DTOs.Precificacao;
using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace API.Tests.Integration;

public sealed class PrecificacaoPipelineTests(ApiTestWebApplicationFactory factory) : IClassFixture<ApiTestWebApplicationFactory>
{
    private static readonly Guid Id = Guid.Parse("d7a6c926-0427-4f5e-9e1e-242eb7a07fef");
    public static IEnumerable<object[]> Rotas()
    {
        foreach(var (metodo,rota) in new[]{("GET","/api/tipos-planta"),("POST","/api/tipos-planta"),("PUT",$"/api/tipos-planta/{Id}"),("PATCH",$"/api/tipos-planta/{Id}/desativar"),
            ("GET","/api/precos-vistoria"),("GET",$"/api/precos-vistoria/por-tipo/{Id}/historico"),("POST",$"/api/precos-vistoria/por-tipo/{Id}"),("PATCH",$"/api/precos-vistoria/por-tipo/{Id}/{Id}/desativar"),
            ("POST","/api/precos-vistoria/simular"),("GET",$"/api/vistorias/{Id}/precificacao")})
            foreach(var role in new[]{"","Usuario"}) yield return [metodo,rota,role];
    }
    [Theory][MemberData(nameof(Rotas))]
    public async Task AutorizacaoObrigatoria(string metodo,string rota,string role)
    {
        using var client=factory.CreateHttpsClient(); if(role!="") client.DefaultRequestHeaders.Authorization=new("Bearer",Token(role));
        using var request=new HttpRequestMessage(new HttpMethod(metodo),rota){Content=JsonContent.Create(new{})};
        Assert.Equal(role==""?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden,(await client.SendAsync(request)).StatusCode);
    }
    [Fact] public async Task AdministradorConsultaCatalogoHistoricoEPrecos()
    {
        var store=new Mock<IPrecificacaoStore>(); store.Setup(s=>s.ListarTiposAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TipoPlantaResponseDto>());
        store.Setup(s=>s.ListarAtivosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<PrecoVistoria>());
        store.Setup(s=>s.HistoricoAsync(Id,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<PrecoVistoria>());
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddScoped(_=>store.Object))); using var client=app.CreateHttpsClient(); client.DefaultRequestHeaders.Authorization=new("Bearer",Token("Administrador"));
        foreach(var route in new[]{"/api/tipos-planta","/api/precos-vistoria",$"/api/precos-vistoria/por-tipo/{Id}/historico"}) Assert.Equal("[]",await client.GetStringAsync(route));
    }
    [Theory][InlineData("nome_duplicado")][InlineData("desative_preco_primeiro")][InlineData("versao_conflitante")][InlineData("preco_ausente")][InlineData("tipo_inativo")]
    public async Task ConflitosRetornamProblemDetailsSeguro(string codigo)
    {
        var service=new Mock<IPrecificacaoService>(); service.Setup(s=>s.SimularAsync(It.IsAny<SimularVistoriaDto>(),It.IsAny<CancellationToken>())).ThrowsAsync(new PrecificacaoException(codigo));
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddScoped(_=>service.Object))); using var client=app.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token("Administrador"));
        var r=await client.PostAsJsonAsync("/api/precos-vistoria/simular",new{tipoPlantaId=Id,areaM2=10,pacote=0});Assert.Equal(HttpStatusCode.Conflict,r.StatusCode); Assert.Contains("Configuração",await r.Content.ReadAsStringAsync());
    }
    [Theory][InlineData("tipoPlanta")][InlineData("valorFinal")][InlineData("valor")]
    public async Task CriacaoVistoriaRejeitaTextoLivreEValorManual(string campo)
    {
        using var client=factory.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token("Administrador"));
        var r=await client.PostAsJsonAsync("/api/vistorias",new Dictionary<string,object>{{"usuarioId",Id},{"tipoPlantaId",Id},{"areaM2",10},{"pacote",0},{"dataAgendada","2026-09-23T14:30"},{campo,"MARCADOR_FICTICIO"}});
        Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);Assert.DoesNotContain("MARCADOR_FICTICIO",await r.Content.ReadAsStringAsync());
    }
    [Theory][InlineData("")][InlineData(" ")]
    public async Task NomeInvalidoNaoPersiste(string nome)
    {
        var store=new Mock<IPrecificacaoStore>();using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddScoped(_=>store.Object)));using var client=app.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token("Administrador"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.PostAsJsonAsync("/api/tipos-planta",new{nome})).StatusCode);Assert.Empty(store.Invocations);
    }
    [Fact] public async Task SimulacaoRetornaDtoSemEscritaOuSegredos()
    {
        var store=new Mock<IPrecificacaoStore>();store.Setup(s=>s.SimularAsync(It.IsAny<SimularVistoriaDto>(),It.IsAny<DateTime>(),It.IsAny<CancellationToken>())).ReturnsAsync(new Domain.Services.CalculoVistoria(Id,1,Id,"Tipo fictício",10,PacoteVistoria.Total,2,ModalidadeAcrescimo.Fixo,5,20,25,DateTime.UtcNow));
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.AddScoped(_=>store.Object)));using var client=app.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token("Administrador"));
        var response=await client.PostAsJsonAsync("/api/precos-vistoria/simular",new{tipoPlantaId=Id,areaM2="10.00",pacote=1});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var dto=await response.Content.ReadFromJsonAsync<CalculoVistoriaResponseDto>();Assert.True(dto!.Simulacao);Assert.Equal(25,dto.ValorFinal);Assert.Single(store.Invocations);
        var json=await response.Content.ReadAsStringAsync();foreach(var palavra in new[]{"chavePix","ciphertext","lease","connectionString","certificado"})Assert.DoesNotContain(palavra,json);
    }
    [Fact] public async Task SemExclusaoFisica()
    { using var client=factory.CreateHttpsClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token("Administrador"));Assert.Equal(HttpStatusCode.MethodNotAllowed,(await client.DeleteAsync($"/api/tipos-planta/{Id}")).StatusCode); }
    [Theory]
    [InlineData(0, 0, 0)] [InlineData(-1, 0, 0)] [InlineData(1, 0, -1)]
    [InlineData(1, 1, 10001)] [InlineData(1, 9, 0)] [InlineData(100000000, 0, 0)]
    public async Task PublicacaoInvalidaNaoAcessaStore(decimal preco, int modalidade, decimal acrescimo)
    {
        var store = new Mock<IPrecificacaoStore>();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => store.Object)));
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        var response = await client.PostAsJsonAsync($"/api/precos-vistoria/por-tipo/{Id}", new { precoM2 = preco, modalidade, acrescimo, versaoEsperada = 0 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(store.Invocations);
    }

    [Theory] [InlineData(0, 0)] [InlineData(-1, 0)] [InlineData(10, 9)]
    public async Task SimulacaoInvalidaNaoAcessaStore(decimal area, int pacote)
    {
        var store = new Mock<IPrecificacaoStore>();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => store.Object)));
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/precos-vistoria/simular", new { tipoPlantaId = Id, areaM2 = area, pacote })).StatusCode);
        Assert.Empty(store.Invocations);
    }

    [Fact]
    public async Task AdministradorPublicaEDesativaSemEditarVersaoHistorica()
    {
        var store = new Mock<IPrecificacaoStore>();
        var preco = new PrecoVistoria(Id, "Tipo fictício", 2, ModalidadeAcrescimo.Fixo, 5, 1, DateTime.UtcNow);
        store.Setup(s => s.PublicarAsync(Id, It.IsAny<PublicarPrecoDto>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(preco);
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => store.Object)));
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        var response = await client.PostAsJsonAsync($"/api/precos-vistoria/por-tipo/{Id}", new { precoM2 = "2.0000", modalidade = 0, acrescimo = "5", versaoEsperada = 0 });
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(preco.Id, (await response.Content.ReadFromJsonAsync<PrecoVistoriaResponseDto>())!.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsync($"/api/precos-vistoria/por-tipo/{Id}/{preco.Id}/desativar", null)).StatusCode);
        store.Verify(s => s.DesativarPrecoAsync(Id, preco.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CriacaoHttpRetornaSnapshotCalculadoELocation()
    {
        var store = new Mock<IPrecificacaoStore>();
        var users = new Mock<Domain.Interfaces.IUsuarioRepository>();
        users.Setup(s => s.ObterPorIdAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(new Usuario("Fictício", "teste@example.invalid", "hash-ficticio", codigoIndicacao: "TEST1234"));
        var instante = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        var calculo = Domain.Services.MotorPrecificacaoVistoria.Calcular(new(Id, "Fictício", 2, ModalidadeAcrescimo.Fixo, 5, 1, instante), "Fictício", 10, PacoteVistoria.Total, instante);
        var vistoria = Vistoria.CriarCalculada(Id, instante, calculo);
        store.Setup(s => s.CriarVistoriaAsync(Id, Id, 10, PacoteVistoria.Total, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(vistoria);
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.AddScoped(_ => store.Object); s.AddScoped(_ => users.Object); }));
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        var response = await client.PostAsJsonAsync("/api/vistorias", new { usuarioId = Id, tipoPlantaId = Id, areaM2 = "10", pacote = 1, dataAgendada = "2026-09-23T14:30" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/vistorias/{vistoria.Id}", response.Headers.Location!.ToString());
        var dto = await response.Content.ReadFromJsonAsync<Application.DTOs.Vistoria.VistoriaResponseDto>();
        Assert.False(dto!.Legado);
        Assert.Equal(25, dto.Precificacao!.ValorFinal);
        Assert.False(dto.Precificacao.Simulacao);
        Assert.Single(store.Invocations);
    }

    [Fact]
    public async Task CancelamentoDoControllerPropagaSemRepetir()
    {
        using var cts = new CancellationTokenSource();
        var service = new Mock<IPrecificacaoService>();
        var dto = new SimularVistoriaDto(Id, 10, PacoteVistoria.Simples);
        service.Setup(s => s.SimularAsync(dto, cts.Token)).ThrowsAsync(new OperationCanceledException(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new API.Controllers.PrecosVistoriaController(service.Object).Simular(dto, cts.Token));
        service.Verify(s => s.SimularAsync(dto, cts.Token), Times.Once);
    }

    [Fact] public void DiResolveServicosScoped()
    { using var scope=factory.Services.CreateScope();Assert.IsType<Application.Services.PrecificacaoService>(scope.ServiceProvider.GetRequiredService<IPrecificacaoService>());Assert.IsType<Infrastructure.Repositories.PrecificacaoMySqlStore>(scope.ServiceProvider.GetRequiredService<IPrecificacaoStore>()); }
    private static string Token(string role)=>new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("IndicA2.Api.Tests","IndicA2.Api.Tests.Client",
        [new Claim("sub",Id.ToString()),new Claim("role",role)],expires:DateTime.UtcNow.AddMinutes(10),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("chave-ficticia-de-autorizacao-com-mais-de-trinta-e-dois-bytes")),SecurityAlgorithms.HmacSha256)));
}

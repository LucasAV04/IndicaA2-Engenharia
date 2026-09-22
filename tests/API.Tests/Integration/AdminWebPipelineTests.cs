using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Collections.Concurrent;
using API.Contracts;
using API.Controllers;
using Application.DTOs.Admin;
using Application.DTOs.DadosPix;
using Application.DTOs.PagamentoPix;
using Application.DTOs.PagamentoVistoria;
using Application.DTOs.Usuario;
using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Domain.Enums;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace API.Tests.Integration;

public sealed class AdminWebPipelineTests(ApiTestWebApplicationFactory factory) : IClassFixture<ApiTestWebApplicationFactory>
{
    public static IEnumerable<object[]> Rotas()
    {
        var id = Guid.NewGuid();
        foreach (var (method, route) in new[] {
            ("GET", "/api/usuarios"), ("POST", "/api/usuarios"), ("GET", $"/api/usuarios/{id}"), ("PUT", $"/api/usuarios/{id}"),
            ("GET", $"/api/usuarios/{id}/dados-pix"), ("PUT", $"/api/usuarios/{id}/dados-pix"), ("DELETE", $"/api/usuarios/{id}/dados-pix"),
            ("GET", "/api/pagamentos-vistoria"), ("POST", "/api/pagamentos-vistoria"), ("GET", $"/api/pagamentos-vistoria/{id}"),
            ("GET", $"/api/pagamentos-vistoria/por-vistoria/{id}"), ("PATCH", $"/api/pagamentos-vistoria/{id}/confirmar"),
            ("PATCH", $"/api/pagamentos-vistoria/{id}/cancelar"), ("GET", "/api/pagamentos-pix"), ("GET", "/api/admin/dashboard")
        }) foreach (var role in new[] { "", "Usuario" }) yield return [method, route, role];
    }

    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task RotasAdministrativasExigemAutenticacaoEAdministrador(string method, string route, string role)
    {
        using var client = factory.CreateHttpsClient();
        if (role != "") client.DefaultRequestHeaders.Authorization = new("Bearer", Token(role));
        using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = JsonContent.Create(new { }) };
        var result = await client.SendAsync(request);
        Assert.Equal(role == "" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, result.StatusCode);
    }

    [Theory]
    [InlineData(TipoChavePix.Cpf, "12345678909", "••••8909")]
    [InlineData(TipoChavePix.Cnpj, "11222333000181", "••••0181")]
    [InlineData(TipoChavePix.Telefone, "+5511999999999", "••••9999")]
    [InlineData(TipoChavePix.Email, "segredo@example.invalid", "***@example.invalid")]
    [InlineData(TipoChavePix.Aleatoria, "21ba422b-962e-4af2-9c2c-bc35ae47d08f", "••••••••")]
    public async Task DadosPixGetEPutRetornamSomenteMascara(TipoChavePix tipo, string chave, string esperado)
    {
        var dto = new DadosPixResponseDto { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), TipoChavePix = tipo, ChavePix = chave };
        var service = new Mock<IDadosPixService>();
        service.Setup(x => x.ObterPorUsuarioIdAsync(dto.UsuarioId, It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        service.Setup(x => x.CadastrarOuAtualizarAsync(dto.UsuarioId, It.IsAny<DadosPixDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => service.Object)));
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(method, $"/api/usuarios/{dto.UsuarioId}/dados-pix") { Content = JsonContent.Create(new DadosPixDto { TipoChavePix = tipo, ChavePix = chave }) };
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(chave, text);
            var result = await response.Content.ReadFromJsonAsync<DadosPixSeguroResponse>();
            Assert.Equal(esperado, result!.ChaveMascarada);
        }
    }

    [Fact]
    public async Task DadosPixAusenteRetorna204ERemocaoIdempotente()
    {
        var service = new Mock<IDadosPixService>();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => service.Object)));
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        var path = $"/api/usuarios/{Guid.NewGuid()}/dados-pix";
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path)).StatusCode);
    }

    [Fact]
    public async Task DadosPixErroNaoExpoeChaveNaResposta()
    {
        var logs = new CapturedLogs();
        var service = new Mock<IDadosPixService>();
        service.Setup(x => x.CadastrarOuAtualizarAsync(It.IsAny<Guid>(), It.IsAny<DadosPixDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("chave-ficticia-que-nao-pode-vazar"));
        using var app = factory.WithWebHostBuilder(b =>
        {
            b.ConfigureTestServices(s => s.AddScoped(_ => service.Object));
            b.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        var response = await client.PutAsJsonAsync($"/api/usuarios/{Guid.NewGuid()}/dados-pix", new DadosPixDto());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("chave-ficticia-que-nao-pode-vazar", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(logs.Messages, message => message.Contains("chave-ficticia-que-nao-pode-vazar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsuarioCriadoRetornaLocationEEdicaoRejeitaIdDivergente()
    {
        var service = new Mock<IUsuarioService>();
        var id = Guid.NewGuid();
        service.Setup(x => x.CriarAsync(It.IsAny<CreateUsuarioDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsuarioResponseDto { Id = id, TipoUsuario = TipoUsuario.Usuario });
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => service.Object)));
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        var response = await client.PostAsJsonAsync("/api/usuarios", new { nome = "Cliente", email = "cliente@example.invalid", senha = "ficticia", tipoUsuario = 2 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/usuarios/{id}", response.Headers.Location!.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("senha", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TipoUsuario.Usuario, (await response.Content.ReadFromJsonAsync<UsuarioResponseDto>())!.TipoUsuario);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/usuarios/{id}", new UpdateUsuarioDto { Id = Guid.NewGuid() })).StatusCode);
        service.Verify(x => x.AtualizarAsync(It.IsAny<UpdateUsuarioDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PagamentoCriadoTemLocationETransicoes204()
    {
        var service = new Mock<IPagamentoVistoriaService>();
        var id = Guid.NewGuid();
        service.Setup(x => x.CriarAsync(It.IsAny<CreatePagamentoVistoriaDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoVistoriaResponseDto { Id = id });
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => service.Object)));
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        var response = await client.PostAsJsonAsync("/api/pagamentos-vistoria", new CreatePagamentoVistoriaDto());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/pagamentos-vistoria/{id}", response.Headers.Location!.ToString());
        foreach (var action in new[] { "confirmar", "cancelar" })
            Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsync($"/api/pagamentos-vistoria/{id}/{action}", null)).StatusCode);
    }

    [Fact]
    public async Task DashboardEPixGlobalRetornamDadosSeguros()
    {
        var dashboard = new Mock<IAdminDashboardStore>();
        dashboard.Setup(x => x.ObterAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DashboardResponseDto { ReceitaConfirmada = 12.34m });
        var pix = new Mock<IPagamentoPixService>();
        pix.Setup(x => x.ObterTodosAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new PagamentoPixResponseDto { Id = Guid.NewGuid(), Valor = 2.47m }]);
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.AddScoped(_ => dashboard.Object); s.AddScoped(_ => pix.Object); }));
        using var client = app.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token("Administrador"));
        Assert.Equal(12.34m, (await client.GetFromJsonAsync<DashboardResponseDto>("/api/admin/dashboard"))!.ReceitaConfirmada);
        var text = await client.GetStringAsync("/api/pagamentos-pix");
        foreach (var sensitive in new[] { "chavePix", "ciphertext", "nonce", "lease", "provider", "certificado" }) Assert.DoesNotContain(sensitive, text);
        Assert.Single((await client.GetFromJsonAsync<PagamentoPixResponseDto[]>("/api/pagamentos-pix"))!);
    }

    [Fact]
    public async Task ControllersPropagamTokenEmConsultasEComandos()
    {
        var token = new CancellationTokenSource().Token;
        var id = Guid.NewGuid();
        var users = new Mock<IUsuarioService>();
        var pix = new Mock<IDadosPixService>();
        var payments = new Mock<IPagamentoVistoriaService>();
        var dash = new Mock<IAdminDashboardStore>();
        await new UsuariosController(users.Object).ObterTodosAsync(token);
        await new UsuariosController(users.Object).AtualizarAsync(id, new UpdateUsuarioDto { Id = id }, token);
        await new DadosPixController(pix.Object).ObterAsync(id, token);
        await new DadosPixController(pix.Object).RemoverAsync(id, token);
        await new PagamentosVistoriaController(payments.Object).ConfirmarAsync(id, token);
        await new PagamentosVistoriaController(payments.Object).CancelarAsync(id, token);
        await new AdminDashboardController(dash.Object).ObterAsync(token);
        users.Verify(x => x.ObterTodosAsync(token), Times.Once);
        users.Verify(x => x.AtualizarAsync(It.IsAny<UpdateUsuarioDto>(), token), Times.Once);
        pix.Verify(x => x.ObterPorUsuarioIdAsync(id, token), Times.Once);
        pix.Verify(x => x.RemoverAsync(id, token), Times.Once);
        payments.Verify(x => x.ConfirmarAsync(id, token), Times.Once);
        payments.Verify(x => x.CancelarAsync(id, token), Times.Once);
        dash.Verify(x => x.ObterAsync(token), Times.Once);
    }

    [Fact]
    public void ApiResolveNovosServicosScopedSemProvider()
    {
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => {
            s.RemoveAll<Infrastructure.Security.IDadosPixProtector>();
            s.AddSingleton(Mock.Of<Infrastructure.Security.IDadosPixProtector>());
        }));
        using var scope = app.Services.CreateScope();
        Assert.IsType<IndicA2.Application.Services.UsuarioService>(scope.ServiceProvider.GetRequiredService<IUsuarioService>());
        Assert.IsType<Application.Services.DadosPixService>(scope.ServiceProvider.GetRequiredService<IDadosPixService>());
        Assert.IsType<Application.Services.PagamentoVistoriaService>(scope.ServiceProvider.GetRequiredService<IPagamentoVistoriaService>());
        Assert.IsType<Infrastructure.Repositories.AdminDashboardMySqlStore>(scope.ServiceProvider.GetRequiredService<IAdminDashboardStore>());
        Assert.IsType<Infrastructure.Repositories.PagamentoPixLeituraAdministrativaMySqlStore>(scope.ServiceProvider.GetRequiredService<IPagamentoPixLeituraAdministrativaStore>());
        Assert.IsType<Application.Services.PagamentoPixService>(scope.ServiceProvider.GetRequiredService<IPagamentoPixService>());
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }

        private sealed class CaptureLogger(ConcurrentBag<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception) + exception);
        }
    }

    private static string Token(string role) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "IndicA2.Api.Tests", "IndicA2.Api.Tests.Client",
        [new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", role)],
        expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("chave-ficticia-de-autorizacao-com-mais-de-trinta-e-dois-bytes")), SecurityAlgorithms.HmacSha256)));
}

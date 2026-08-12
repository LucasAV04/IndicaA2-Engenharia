using API.Controllers;
using API.Tests.Authorization;
using Application.DTOs.Vistoria;
using Application.Interfaces.Services;
using Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class VistoriasControllerTests
{
    [Fact]
    public async Task CriarAsync_DeveRetornarCreatedAtActionERepassarCancellationToken()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var dto = CriarDto();
        var resposta = CriarResposta();
        var service = new Mock<IVistoriaService>();
        service.Setup(item => item.CriarAsync(dto, cancellationToken)).ReturnsAsync(resposta);
        var controller = ControllerAuthorizationFactory.CriarVistoriasController(service.Object);

        var resultado = await controller.CriarAsync(dto, cancellationToken);

        var created = Assert.IsType<CreatedAtActionResult>(resultado.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(nameof(VistoriasController.ObterPorIdAsync), created.ActionName);
        Assert.Equal(resposta, created.Value);
        service.Verify(item => item.CriarAsync(dto, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveRetornarOk()
    {
        var id = Guid.NewGuid();
        var resposta = CriarResposta(id);
        var service = new Mock<IVistoriaService>();
        service.Setup(item => item.ObterPorIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(resposta);
        var controller = ControllerAuthorizationFactory.CriarVistoriasController(service.Object);

        var resultado = await controller.ObterPorIdAsync(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Equal(resposta, ok.Value);
    }

    [Fact]
    public async Task ObterTodasAsync_QuandoNaoHouverRegistros_DeveRetornarOkComColecaoVazia()
    {
        IReadOnlyCollection<VistoriaResponseDto> respostas = Array.Empty<VistoriaResponseDto>();
        var service = new Mock<IVistoriaService>();
        service.Setup(item => item.ObterTodasAsync(It.IsAny<CancellationToken>())).ReturnsAsync(respostas);
        var controller = ControllerAuthorizationFactory.CriarVistoriasController(service.Object);

        var resultado = await controller.ObterTodasAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Same(respostas, ok.Value);
    }

    [Fact]
    public async Task ObterPorUsuarioIdAsync_DeveRetornarOk()
    {
        var usuarioId = Guid.NewGuid();
        IReadOnlyCollection<VistoriaResponseDto> respostas = [CriarResposta()];
        var service = new Mock<IVistoriaService>();
        service.Setup(item => item.ObterPorUsuarioIdAsync(usuarioId, It.IsAny<CancellationToken>())).ReturnsAsync(respostas);
        var controller = ControllerAuthorizationFactory.CriarVistoriasController(service.Object);

        var resultado = await controller.ObterPorUsuarioIdAsync(usuarioId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Same(respostas, ok.Value);
    }

    [Fact]
    public async Task MarcarRealizadaAsync_DeveRetornarNoContentERepassarCancellationToken()
    {
        var id = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var service = new Mock<IVistoriaService>();
        var controller = ControllerAuthorizationFactory.CriarVistoriasController(service.Object);

        var resultado = await controller.MarcarRealizadaAsync(id, cancellationToken);

        Assert.IsType<NoContentResult>(resultado);
        service.Verify(item => item.MarcarRealizadaAsync(id, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ConcluirAsync_DeveRetornarNoContent()
    {
        var id = Guid.NewGuid();
        var service = new Mock<IVistoriaService>();
        var controller = ControllerAuthorizationFactory.CriarVistoriasController(service.Object);

        var resultado = await controller.ConcluirAsync(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(resultado);
        service.Verify(item => item.ConcluirAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_DeveRetornarNoContent()
    {
        var id = Guid.NewGuid();
        var service = new Mock<IVistoriaService>();
        var controller = ControllerAuthorizationFactory.CriarVistoriasController(service.Object);

        var resultado = await controller.CancelarAsync(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(resultado);
        service.Verify(item => item.CancelarAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObterPorIdAsync_QuandoNaoForOwnerNemAdministrador_DeveRetornarForbidden()
    {
        var id = Guid.NewGuid();
        var service = new Mock<IVistoriaService>();
        service.Setup(item => item.ObterPorIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(CriarResposta(id));
        var controller = ControllerAuthorizationFactory.CriarVistoriasController(service.Object, authorizationSucceeded: false);

        var resultado = await controller.ObterPorIdAsync(id, CancellationToken.None);

        Assert.IsType<ForbidResult>(resultado.Result);
    }

    [Fact]
    public async Task ObterPorUsuarioIdAsync_QuandoIdNaoForDoUsuarioAtual_DeveRetornarForbidden()
    {
        var service = new Mock<IVistoriaService>();
        var controller = ControllerAuthorizationFactory.CriarVistoriasController(service.Object, canAccessUser: false);

        var resultado = await controller.ObterPorUsuarioIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ForbidResult>(resultado.Result);
        service.Verify(item => item.ObterPorUsuarioIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateVistoriaDto CriarDto() => new()
    {
        UsuarioId = Guid.NewGuid(),
        TipoPlanta = "Apartamento",
        AreaM2 = 75.5m,
        Pacote = PacoteVistoria.Simples,
        DataAgendada = new DateTime(2026, 9, 15, 14, 30, 0, DateTimeKind.Unspecified)
    };

    private static VistoriaResponseDto CriarResposta(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        UsuarioId = Guid.NewGuid(),
        TipoPlanta = "Apartamento",
        AreaM2 = 75.5m,
        Pacote = PacoteVistoria.Simples,
        DataAgendada = new DateTime(2026, 9, 15, 14, 30, 0, DateTimeKind.Unspecified),
        Status = StatusVistoria.Agendada,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

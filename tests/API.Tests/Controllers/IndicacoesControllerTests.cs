using API.Controllers;
using Application.DTOs.Indicacao;
using Application.Interfaces.Services;
using Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class IndicacoesControllerTests
{
    [Fact]
    public async Task CriarAsync_DeveRetornarCreatedAtActionERepassarCancellationToken()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var dto = CriarDto();
        var resposta = CriarResposta();
        var service = new Mock<IIndicacaoService>();
        service.Setup(s => s.CriarAsync(dto, cancellationToken)).ReturnsAsync(resposta);
        var controller = new IndicacoesController(service.Object);

        var resultado = await controller.CriarAsync(dto, cancellationToken);

        var created = Assert.IsType<CreatedAtActionResult>(resultado.Result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(nameof(IndicacoesController.ObterPorIdAsync), created.ActionName);
        Assert.Equal(resposta, created.Value);
        service.Verify(s => s.CriarAsync(dto, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveRetornarOk()
    {
        var id = Guid.NewGuid();
        var resposta = CriarResposta(id);
        var service = new Mock<IIndicacaoService>();
        service.Setup(s => s.ObterPorIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(resposta);
        var controller = new IndicacoesController(service.Object);

        var resultado = await controller.ObterPorIdAsync(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Equal(resposta, ok.Value);
    }

    [Fact]
    public async Task ObterTodasAsync_QuandoNaoHouverRegistros_DeveRetornarOkComColecaoVazia()
    {
        IReadOnlyCollection<IndicacaoResponseDto> respostas = Array.Empty<IndicacaoResponseDto>();
        var service = new Mock<IIndicacaoService>();
        service.Setup(s => s.ObterTodasAsync(It.IsAny<CancellationToken>())).ReturnsAsync(respostas);
        var controller = new IndicacoesController(service.Object);

        var resultado = await controller.ObterTodasAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        Assert.Same(respostas, ok.Value);
    }

    [Fact]
    public async Task CancelarAsync_DeveRetornarNoContent()
    {
        var id = Guid.NewGuid();
        var service = new Mock<IIndicacaoService>();
        var controller = new IndicacoesController(service.Object);

        var resultado = await controller.CancelarAsync(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(resultado);
        service.Verify(s => s.CancelarAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VincularVistoriaAsync_DeveRetornarNoContent()
    {
        var id = Guid.NewGuid();
        var dto = new VincularVistoriaDto { IndicacaoId = id, VistoriaId = Guid.NewGuid() };
        var service = new Mock<IIndicacaoService>();
        var controller = new IndicacoesController(service.Object);

        var resultado = await controller.VincularVistoriaAsync(id, dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(resultado);
        service.Verify(s => s.VincularVistoriaAsync(dto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VincularUsuarioIndicadoAsync_DeveRetornarNoContent()
    {
        var id = Guid.NewGuid();
        var dto = new VincularUsuarioIndicadoDto { IndicacaoId = id, UsuarioIndicadoId = Guid.NewGuid() };
        var service = new Mock<IIndicacaoService>();
        var controller = new IndicacoesController(service.Object);

        var resultado = await controller.VincularUsuarioIndicadoAsync(id, dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(resultado);
        service.Verify(s => s.VincularUsuarioIndicadoAsync(dto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VincularVistoriaAsync_QuandoIdsDivergirem_DeveRetornarValidationProblem()
    {
        var service = new Mock<IIndicacaoService>();
        var controller = new IndicacoesController(service.Object);

        var resultado = await controller.VincularVistoriaAsync(
            Guid.NewGuid(),
            new VincularVistoriaDto { IndicacaoId = Guid.NewGuid(), VistoriaId = Guid.NewGuid() },
            CancellationToken.None);

        var problem = Assert.IsType<BadRequestObjectResult>(resultado);
        var details = Assert.IsType<ValidationProblemDetails>(problem.Value);
        service.Verify(s => s.VincularVistoriaAsync(
            It.IsAny<VincularVistoriaDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObterPorStatusAsync_QuandoStatusForInvalido_DeveRetornarValidationProblem()
    {
        var service = new Mock<IIndicacaoService>();
        var controller = new IndicacoesController(service.Object);

        var resultado = await controller.ObterPorStatusAsync((StatusIndicacao)99, CancellationToken.None);

        var problem = Assert.IsType<BadRequestObjectResult>(resultado.Result);
        var details = Assert.IsType<ValidationProblemDetails>(problem.Value);
        service.Verify(s => s.ObterPorStatusAsync(It.IsAny<StatusIndicacao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateIndicacaoDto CriarDto() => new()
    {
        UsuarioIndicadorId = Guid.NewGuid(),
        NomeIndicada = "Ana Indicada",
        TelefoneIndicada = "11999999999",
        CodigoIndicacaoUsado = "A2-123"
    };

    private static IndicacaoResponseDto CriarResposta(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        UsuarioIndicadorId = Guid.NewGuid(),
        NomeIndicada = "Ana Indicada",
        TelefoneIndicada = "11999999999",
        CodigoIndicacaoUsado = "A2-123",
        Status = StatusIndicacao.Pendente,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

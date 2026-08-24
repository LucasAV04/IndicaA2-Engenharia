using API.Controllers;
using Application.DTOs.Cashback;
using Application.Interfaces.Services;
using Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class CashbacksControllerTests
{
    [Fact]
    public async Task GerarPorPagamentoAsync_DeveRetornarCreatedAtActionERepassarCancellationToken()
    {
        var pagamentoId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var resposta = CriarResposta();
        var service = new Mock<ICashbackService>();
        service.Setup(item => item.GerarPorPagamentoAsync(pagamentoId, cancellationToken)).ReturnsAsync(resposta);
        var controller = new CashbacksController(service.Object);

        var resultado = await controller.GerarPorPagamentoAsync(pagamentoId, cancellationToken);

        var created = Assert.IsType<CreatedAtActionResult>(resultado.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(nameof(CashbacksController.ObterPorIdAsync), created.ActionName);
        Assert.Equal(resposta, created.Value);
    }

    [Fact]
    public async Task Consultas_DevemRetornarOk()
    {
        var resposta = CriarResposta();
        IReadOnlyCollection<CashbackResponseDto> colecao = [resposta];
        var service = new Mock<ICashbackService>();
        service.Setup(item => item.ObterPorIdAsync(resposta.Id, It.IsAny<CancellationToken>())).ReturnsAsync(resposta);
        service.Setup(item => item.ObterPorPagamentoVistoriaIdAsync(resposta.PagamentoVistoriaId, It.IsAny<CancellationToken>())).ReturnsAsync(resposta);
        service.Setup(item => item.ObterTodosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(colecao);
        service.Setup(item => item.ObterPorUsuarioIndicadorIdAsync(resposta.UsuarioIndicadorId, It.IsAny<CancellationToken>())).ReturnsAsync(colecao);
        var controller = new CashbacksController(service.Object);

        var porId = await controller.ObterPorIdAsync(resposta.Id, CancellationToken.None);
        var porPagamento = await controller.ObterPorPagamentoAsync(resposta.PagamentoVistoriaId, CancellationToken.None);
        var todos = await controller.ObterTodosAsync(CancellationToken.None);
        var porIndicador = await controller.ObterPorIndicadorAsync(resposta.UsuarioIndicadorId, CancellationToken.None);

        Assert.Equal(resposta, Assert.IsType<OkObjectResult>(porId.Result).Value);
        Assert.Equal(resposta, Assert.IsType<OkObjectResult>(porPagamento.Result).Value);
        Assert.Same(colecao, Assert.IsType<OkObjectResult>(todos.Result).Value);
        Assert.Same(colecao, Assert.IsType<OkObjectResult>(porIndicador.Result).Value);
    }

    [Fact]
    public async Task AprovarECancelarAsync_DevemRetornarNoContent()
    {
        var id = Guid.NewGuid();
        var service = new Mock<ICashbackService>();
        var controller = new CashbacksController(service.Object);

        var aprovar = await controller.AprovarAsync(id, CancellationToken.None);
        var cancelar = await controller.CancelarAsync(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(aprovar);
        Assert.IsType<NoContentResult>(cancelar);
        service.Verify(item => item.AprovarAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(item => item.CancelarAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GerarPorPagamentoAsync_NaoPossuiDtoFinanceiroNoContratoHttp()
    {
        var method = typeof(CashbacksController).GetMethod(nameof(CashbacksController.GerarPorPagamentoAsync))!;
        var parameters = method.GetParameters();

        Assert.Collection(parameters,
            parameter => Assert.Equal(typeof(Guid), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    private static CashbackResponseDto CriarResposta() => new()
    {
        Id = Guid.NewGuid(),
        IndicacaoId = Guid.NewGuid(),
        PagamentoVistoriaId = Guid.NewGuid(),
        UsuarioIndicadorId = Guid.NewGuid(),
        ValorTotalPago = 500m,
        Percentual = 0.20m,
        Valor = 100m,
        Status = StatusCashback.Pendente,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

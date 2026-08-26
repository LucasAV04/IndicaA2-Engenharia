using API.Controllers;
using Application.DTOs.PagamentoPix;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class PagamentosPixControllerTests
{
    [Fact]
    public async Task CriarPorCashbackAsync_DeveRetornarCreatedAtActionERepassarCancellationToken()
    {
        var cashbackId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var resposta = CriarResposta();
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.CriarPorCashbackAsync(cashbackId, cancellationToken)).ReturnsAsync(resposta);
        var controller = new PagamentosPixController(service.Object);

        var resultado = await controller.CriarPorCashbackAsync(cashbackId, cancellationToken);

        var created = Assert.IsType<CreatedAtActionResult>(resultado.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(nameof(PagamentosPixController.ObterPorIdAsync), created.ActionName);
        Assert.Equal(resposta, created.Value);
    }

    [Fact]
    public async Task Consultas_DevemRetornarOkERepassarCancellationToken()
    {
        var resposta = CriarResposta();
        IReadOnlyCollection<PagamentoPixResponseDto> colecao = [resposta];
        var cancellationToken = new CancellationTokenSource().Token;
        var service = new Mock<IPagamentoPixService>();
        service.Setup(item => item.ObterPorIdAsync(resposta.Id, cancellationToken)).ReturnsAsync(resposta);
        service.Setup(item => item.ObterPorCashbackIdAsync(resposta.CashbackId, cancellationToken)).ReturnsAsync(resposta);
        service.Setup(item => item.ObterPorUsuarioBeneficiarioIdAsync(resposta.UsuarioBeneficiarioId, cancellationToken))
            .ReturnsAsync(colecao);
        var controller = new PagamentosPixController(service.Object);

        var porId = await controller.ObterPorIdAsync(resposta.Id, cancellationToken);
        var porCashback = await controller.ObterPorCashbackAsync(resposta.CashbackId, cancellationToken);
        var porBeneficiario = await controller.ObterPorBeneficiarioAsync(resposta.UsuarioBeneficiarioId, cancellationToken);

        Assert.Equal(resposta, Assert.IsType<OkObjectResult>(porId.Result).Value);
        Assert.Equal(resposta, Assert.IsType<OkObjectResult>(porCashback.Result).Value);
        Assert.Same(colecao, Assert.IsType<OkObjectResult>(porBeneficiario.Result).Value);
    }

    [Fact]
    public async Task CancelarAsync_DeveRetornarNoContentERepassarCancellationToken()
    {
        var id = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;
        var service = new Mock<IPagamentoPixService>();
        var controller = new PagamentosPixController(service.Object);

        var resultado = await controller.CancelarAsync(id, cancellationToken);

        Assert.IsType<NoContentResult>(resultado);
        service.Verify(item => item.CancelarAsync(id, cancellationToken), Times.Once);
    }

    [Fact]
    public void PagamentoPixResponseDto_NaoExpoeChavePixOuMaterialCriptografico()
    {
        var propriedades = typeof(PagamentoPixResponseDto).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain(nameof(PagamentoPix.ChavePix), propriedades);
        Assert.DoesNotContain("Ciphertext", propriedades);
        Assert.DoesNotContain("Nonce", propriedades);
        Assert.DoesNotContain("Tag", propriedades);
        Assert.DoesNotContain("EncryptionVersion", propriedades);
    }

    private static PagamentoPixResponseDto CriarResposta() => new()
    {
        Id = Guid.NewGuid(),
        CashbackId = Guid.NewGuid(),
        UsuarioBeneficiarioId = Guid.NewGuid(),
        Valor = 100m,
        TipoChavePix = TipoChavePix.Email,
        Status = StatusPagamentoPix.Pendente,
        QuantidadeTentativas = 0,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

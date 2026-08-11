using API.Controllers;
using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task LoginAsync_DeveRetornarOkERepassarCancellationToken()
    {
        var dto = new LoginRequestDto { Email = "ana@exemplo.com", Senha = "senha" };
        var token = new CancellationTokenSource().Token;
        var response = new LoginResponseDto { AccessToken = "token", UsuarioId = Guid.NewGuid(), Nome = "Ana", Email = dto.Email };
        var service = new Mock<IAuthService>();
        service.Setup(item => item.LoginAsync(dto, token)).ReturnsAsync(response);
        var controller = new AuthController(service.Object);

        var result = await controller.LoginAsync(dto, token);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(response, ok.Value);
        service.Verify(item => item.LoginAsync(dto, token), Times.Once);
    }
}

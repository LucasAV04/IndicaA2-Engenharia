using API.Controllers;
using API.Security;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace API.Tests.Authorization;

internal static class ControllerAuthorizationFactory
{
    public static IndicacoesController CriarIndicacoesController(
        IIndicacaoService service,
        bool canAccessUser = true,
        bool authorizationSucceeded = true) =>
        CriarContexto(new IndicacoesController(
            service,
            CriarAuthorizationService(authorizationSucceeded),
            CriarCurrentUser(canAccessUser)));

    public static VistoriasController CriarVistoriasController(
        IVistoriaService service,
        bool canAccessUser = true,
        bool authorizationSucceeded = true) =>
        CriarContexto(new VistoriasController(
            service,
            CriarAuthorizationService(authorizationSucceeded),
            CriarCurrentUser(canAccessUser)));

    private static IAuthorizationService CriarAuthorizationService(bool authorizationSucceeded)
    {
        var authorizationService = new Mock<IAuthorizationService>();
        authorizationService
            .Setup(item => item.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                It.IsAny<string>()))
            .ReturnsAsync(authorizationSucceeded ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        return authorizationService.Object;
    }

    private static ICurrentUser CriarCurrentUser(bool canAccessUser)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(item => item.CanAccessUser(It.IsAny<Guid>())).Returns(canAccessUser);
        return currentUser.Object;
    }

    private static TController CriarContexto<TController>(TController controller)
        where TController : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Bearer"))
            }
        };

        return controller;
    }
}

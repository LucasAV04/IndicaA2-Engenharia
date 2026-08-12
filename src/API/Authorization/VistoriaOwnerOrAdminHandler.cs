using API.Security;
using Application.DTOs.Vistoria;
using Microsoft.AspNetCore.Authorization;

namespace API.Authorization;

public sealed class VistoriaOwnerOrAdminHandler(ICurrentUser currentUser)
    : AuthorizationHandler<VistoriaOwnerOrAdminRequirement, VistoriaResponseDto>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        VistoriaOwnerOrAdminRequirement requirement,
        VistoriaResponseDto resource)
    {
        if (currentUser.CanAccessUser(resource.UsuarioId))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

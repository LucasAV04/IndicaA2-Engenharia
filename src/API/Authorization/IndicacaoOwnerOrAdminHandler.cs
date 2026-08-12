using API.Security;
using Application.DTOs.Indicacao;
using Microsoft.AspNetCore.Authorization;

namespace API.Authorization;

public sealed class IndicacaoOwnerOrAdminHandler(ICurrentUser currentUser)
    : AuthorizationHandler<IndicacaoOwnerOrAdminRequirement, IndicacaoResponseDto>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        IndicacaoOwnerOrAdminRequirement requirement,
        IndicacaoResponseDto resource)
    {
        if (currentUser.CanAccessUser(resource.UsuarioIndicadorId))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

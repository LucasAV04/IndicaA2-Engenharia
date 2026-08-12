using API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

public abstract class AuthorizedControllerBase(
    IAuthorizationService authorizationService,
    ICurrentUser currentUser) : ControllerBase
{
    protected bool CanAccessUser(Guid userId) => currentUser.CanAccessUser(userId);

    protected async Task<bool> IsAuthorizedAsync(object resource, string policy) =>
        (await authorizationService.AuthorizeAsync(User, resource, policy)).Succeeded;
}

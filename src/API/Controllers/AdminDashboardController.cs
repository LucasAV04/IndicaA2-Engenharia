using API.Security;
using Application.DTOs.Admin;
using Application.Interfaces.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Policy = AuthorizationPolicies.Administrador)]
public sealed class AdminDashboardController(IAdminDashboardStore dashboard) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponseDto>> ObterAsync(CancellationToken cancellationToken) =>
        Ok(await dashboard.ObterAsync(cancellationToken));
}

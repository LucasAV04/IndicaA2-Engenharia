using API.Contracts;
using API.Security;
using Application.DTOs.DadosPix;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/usuarios/{usuarioId:guid}/dados-pix")]
[Authorize(Policy = AuthorizationPolicies.Administrador)]
public sealed class DadosPixController(IDadosPixService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(DadosPixSeguroResponse), 200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DadosPixSeguroResponse>> ObterAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var dto = await service.ObterPorUsuarioIdAsync(usuarioId, cancellationToken);
        return dto is null ? NoContent() : Ok(DadosPixSeguroResponse.From(dto));
    }

    [HttpPut]
    public async Task<ActionResult<DadosPixSeguroResponse>> SalvarAsync(Guid usuarioId, DadosPixDto dto, CancellationToken cancellationToken) =>
        Ok(DadosPixSeguroResponse.From(await service.CadastrarOuAtualizarAsync(usuarioId, dto, cancellationToken)));

    [HttpDelete]
    public async Task<IActionResult> RemoverAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        await service.RemoverAsync(usuarioId, cancellationToken);
        return NoContent();
    }
}

using API.Security;
using Application.DTOs.Usuario;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/usuarios")]
[Authorize(Policy = AuthorizationPolicies.Administrador)]
public sealed class UsuariosController(IUsuarioService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UsuarioResponseDto>> CriarAsync(CreateUsuarioDto dto, CancellationToken cancellationToken)
    {
        var usuario = await service.CriarAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(ObterPorIdAsync), new { id = usuario.Id }, usuario);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<UsuarioResponseDto>>> ObterTodosAsync(CancellationToken cancellationToken) =>
        Ok(await service.ObterTodosAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [ActionName(nameof(ObterPorIdAsync))]
    public async Task<ActionResult<UsuarioResponseDto>> ObterPorIdAsync(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.ObterPorIdAsync(id, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> AtualizarAsync(Guid id, UpdateUsuarioDto dto, CancellationToken cancellationToken)
    {
        if (id != dto.Id) return BadRequest(new ProblemDetails { Title = "O identificador da rota deve coincidir com o corpo.", Status = 400 });
        await service.AtualizarAsync(dto, cancellationToken);
        return NoContent();
    }
}

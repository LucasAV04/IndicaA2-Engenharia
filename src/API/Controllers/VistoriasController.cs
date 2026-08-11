using Application.DTOs.Vistoria;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/vistorias")]
public sealed class VistoriasController(IVistoriaService vistoriaService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(VistoriaResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VistoriaResponseDto>> CriarAsync(
        [FromBody] CreateVistoriaDto dto,
        CancellationToken cancellationToken)
    {
        var vistoria = await vistoriaService.CriarAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(ObterPorIdAsync), new { id = vistoria.Id }, vistoria);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(VistoriaResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VistoriaResponseDto>> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var vistoria = await vistoriaService.ObterPorIdAsync(id, cancellationToken);
        return Ok(vistoria);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<VistoriaResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<VistoriaResponseDto>>> ObterTodasAsync(
        CancellationToken cancellationToken)
    {
        var vistorias = await vistoriaService.ObterTodasAsync(cancellationToken);
        return Ok(vistorias);
    }

    [HttpGet("por-usuario/{usuarioId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyCollection<VistoriaResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<VistoriaResponseDto>>> ObterPorUsuarioIdAsync(
        Guid usuarioId,
        CancellationToken cancellationToken)
    {
        var vistorias = await vistoriaService.ObterPorUsuarioIdAsync(usuarioId, cancellationToken);
        return Ok(vistorias);
    }

    [HttpPatch("{id:guid}/realizar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> MarcarRealizadaAsync(Guid id, CancellationToken cancellationToken)
    {
        await vistoriaService.MarcarRealizadaAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/concluir")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConcluirAsync(Guid id, CancellationToken cancellationToken)
    {
        await vistoriaService.ConcluirAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/cancelar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelarAsync(Guid id, CancellationToken cancellationToken)
    {
        await vistoriaService.CancelarAsync(id, cancellationToken);
        return NoContent();
    }
}

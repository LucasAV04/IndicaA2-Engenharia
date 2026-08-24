using API.Security;
using Application.DTOs.Cashback;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/cashbacks")]
[Authorize(Policy = AuthorizationPolicies.Administrador)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class CashbacksController(ICashbackService cashbackService) : ControllerBase
{
    [HttpPost("por-pagamento/{pagamentoVistoriaId:guid}")]
    [ProducesResponseType(typeof(CashbackResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CashbackResponseDto>> GerarPorPagamentoAsync(
        Guid pagamentoVistoriaId,
        CancellationToken cancellationToken)
    {
        var cashback = await cashbackService.GerarPorPagamentoAsync(pagamentoVistoriaId, cancellationToken);
        return CreatedAtAction(nameof(ObterPorIdAsync), new { id = cashback.Id }, cashback);
    }

    [HttpGet("{id:guid}")]
    [ActionName(nameof(ObterPorIdAsync))]
    [ProducesResponseType(typeof(CashbackResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CashbackResponseDto>> ObterPorIdAsync(Guid id, CancellationToken cancellationToken) =>
        Ok(await cashbackService.ObterPorIdAsync(id, cancellationToken));

    [HttpGet("por-pagamento/{pagamentoVistoriaId:guid}")]
    [ProducesResponseType(typeof(CashbackResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CashbackResponseDto>> ObterPorPagamentoAsync(
        Guid pagamentoVistoriaId,
        CancellationToken cancellationToken) =>
        Ok(await cashbackService.ObterPorPagamentoVistoriaIdAsync(pagamentoVistoriaId, cancellationToken));

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<CashbackResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CashbackResponseDto>>> ObterTodosAsync(
        CancellationToken cancellationToken) =>
        Ok(await cashbackService.ObterTodosAsync(cancellationToken));

    [HttpGet("por-indicador/{usuarioIndicadorId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyCollection<CashbackResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CashbackResponseDto>>> ObterPorIndicadorAsync(
        Guid usuarioIndicadorId,
        CancellationToken cancellationToken) =>
        Ok(await cashbackService.ObterPorUsuarioIndicadorIdAsync(usuarioIndicadorId, cancellationToken));

    [HttpPatch("{id:guid}/aprovar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AprovarAsync(Guid id, CancellationToken cancellationToken)
    {
        await cashbackService.AprovarAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/cancelar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelarAsync(Guid id, CancellationToken cancellationToken)
    {
        await cashbackService.CancelarAsync(id, cancellationToken);
        return NoContent();
    }
}

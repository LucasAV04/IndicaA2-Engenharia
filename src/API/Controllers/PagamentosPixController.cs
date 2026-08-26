using API.Security;
using Application.DTOs.PagamentoPix;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/pagamentos-pix")]
[Authorize(Policy = AuthorizationPolicies.Administrador)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class PagamentosPixController(IPagamentoPixService pagamentoPixService) : ControllerBase
{
    [HttpPost("por-cashback/{cashbackId:guid}")]
    [ProducesResponseType(typeof(PagamentoPixResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagamentoPixResponseDto>> CriarPorCashbackAsync(
        Guid cashbackId,
        CancellationToken cancellationToken)
    {
        var pagamentoPix = await pagamentoPixService.CriarPorCashbackAsync(cashbackId, cancellationToken);
        return CreatedAtAction(nameof(ObterPorIdAsync), new { id = pagamentoPix.Id }, pagamentoPix);
    }

    [HttpGet("{id:guid}")]
    [ActionName(nameof(ObterPorIdAsync))]
    [ProducesResponseType(typeof(PagamentoPixResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagamentoPixResponseDto>> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await pagamentoPixService.ObterPorIdAsync(id, cancellationToken));

    [HttpGet("por-cashback/{cashbackId:guid}")]
    [ProducesResponseType(typeof(PagamentoPixResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagamentoPixResponseDto>> ObterPorCashbackAsync(
        Guid cashbackId,
        CancellationToken cancellationToken) =>
        Ok(await pagamentoPixService.ObterPorCashbackIdAsync(cashbackId, cancellationToken));

    [HttpGet("por-beneficiario/{usuarioId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyCollection<PagamentoPixResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<PagamentoPixResponseDto>>> ObterPorBeneficiarioAsync(
        Guid usuarioId,
        CancellationToken cancellationToken) =>
        Ok(await pagamentoPixService.ObterPorUsuarioBeneficiarioIdAsync(usuarioId, cancellationToken));

    [HttpPatch("{id:guid}/cancelar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelarAsync(Guid id, CancellationToken cancellationToken)
    {
        await pagamentoPixService.CancelarAsync(id, cancellationToken);
        return NoContent();
    }
}

using API.Security;
using Application.DTOs.PagamentoVistoria;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/pagamentos-vistoria")]
[Authorize(Policy = AuthorizationPolicies.Administrador)]
public sealed class PagamentosVistoriaController(IPagamentoVistoriaService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PagamentoVistoriaResponseDto>> CriarAsync(CreatePagamentoVistoriaDto dto, CancellationToken cancellationToken)
    {
        var pagamento = await service.CriarAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(ObterPorIdAsync), new { id = pagamento.Id }, pagamento);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PagamentoVistoriaResponseDto>>> ObterTodosAsync(CancellationToken cancellationToken) =>
        Ok(await service.ObterTodosAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [ActionName(nameof(ObterPorIdAsync))]
    public async Task<ActionResult<PagamentoVistoriaResponseDto>> ObterPorIdAsync(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.ObterPorIdAsync(id, cancellationToken));

    [HttpGet("por-vistoria/{vistoriaId:guid}")]
    public async Task<ActionResult<PagamentoVistoriaResponseDto>> ObterPorVistoriaAsync(Guid vistoriaId, CancellationToken cancellationToken) =>
        Ok(await service.ObterPorVistoriaIdAsync(vistoriaId, cancellationToken));

    [HttpPatch("{id:guid}/confirmar")]
    public async Task<IActionResult> ConfirmarAsync(Guid id, CancellationToken cancellationToken)
    {
        await service.ConfirmarAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/cancelar")]
    public async Task<IActionResult> CancelarAsync(Guid id, CancellationToken cancellationToken)
    {
        await service.CancelarAsync(id, cancellationToken);
        return NoContent();
    }
}

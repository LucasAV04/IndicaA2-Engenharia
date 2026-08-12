using API.Security;
using Application.DTOs.Indicacao;
using Application.Interfaces.Services;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/indicacoes")]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class IndicacoesController(
    IIndicacaoService indicacaoService,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser)
    : AuthorizedControllerBase(authorizationService, currentUser)
{
    [HttpPost]
    [ProducesResponseType(typeof(IndicacaoResponseDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<IndicacaoResponseDto>> CriarAsync(
        [FromBody] CreateIndicacaoDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanAccessUser(dto.UsuarioIndicadorId))
            return Forbid();

        var indicacao = await indicacaoService.CriarAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(ObterPorIdAsync), new { id = indicacao.Id }, indicacao);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(IndicacaoResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<IndicacaoResponseDto>> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var indicacao = await indicacaoService.ObterPorIdAsync(id, cancellationToken);

        if (!await IsAuthorizedAsync(indicacao, AuthorizationPolicies.IndicacaoOwnerOrAdmin))
            return Forbid();

        return Ok(indicacao);
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Administrador)]
    [ProducesResponseType(typeof(IReadOnlyCollection<IndicacaoResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<IndicacaoResponseDto>>> ObterTodasAsync(
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.Administrador))
            return Forbid();

        var indicacoes = await indicacaoService.ObterTodasAsync(cancellationToken);
        return Ok(indicacoes);
    }

    [HttpGet("por-indicador/{usuarioIndicadorId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyCollection<IndicacaoResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<IndicacaoResponseDto>>> ObterPorUsuarioIndicadorAsync(
        Guid usuarioIndicadorId,
        CancellationToken cancellationToken)
    {
        if (!CanAccessUser(usuarioIndicadorId))
            return Forbid();

        var indicacoes = await indicacaoService.ObterPorUsuarioIndicadorIdAsync(
            usuarioIndicadorId,
            cancellationToken);
        return Ok(indicacoes);
    }

    [HttpGet("por-status/{status}")]
    [Authorize(Policy = AuthorizationPolicies.Administrador)]
    [ProducesResponseType(typeof(IReadOnlyCollection<IndicacaoResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<IndicacaoResponseDto>>> ObterPorStatusAsync(
        StatusIndicacao status,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.Administrador))
            return Forbid();

        if (!Enum.IsDefined(status))
        {
            ModelState.AddModelError(nameof(status), "O status informado é inválido.");
            return BadRequest(new ValidationProblemDetails(ModelState));
        }

        var indicacoes = await indicacaoService.ObterPorStatusAsync(status, cancellationToken);
        return Ok(indicacoes);
    }

    [HttpPatch("{id:guid}/usuario-indicado")]
    [Authorize(Policy = AuthorizationPolicies.Administrador)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> VincularUsuarioIndicadoAsync(
        Guid id,
        [FromBody] VincularUsuarioIndicadoDto dto,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.Administrador))
            return Forbid();

        if (dto.IndicacaoId != id)
        {
            ModelState.AddModelError(nameof(dto.IndicacaoId), "O identificador da rota deve ser igual ao identificador informado no corpo.");
            return BadRequest(new ValidationProblemDetails(ModelState));
        }

        await indicacaoService.VincularUsuarioIndicadoAsync(dto, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/vistoria")]
    [Authorize(Policy = AuthorizationPolicies.Administrador)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> VincularVistoriaAsync(
        Guid id,
        [FromBody] VincularVistoriaDto dto,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.Administrador))
            return Forbid();

        if (dto.IndicacaoId != id)
        {
            ModelState.AddModelError(nameof(dto.IndicacaoId), "O identificador da rota deve ser igual ao identificador informado no corpo.");
            return BadRequest(new ValidationProblemDetails(ModelState));
        }

        await indicacaoService.VincularVistoriaAsync(dto, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/vistoria/concluir")]
    [Authorize(Policy = AuthorizationPolicies.Administrador)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarcarVistoriaConcluidaAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.Administrador))
            return Forbid();

        await indicacaoService.MarcarVistoriaConcluidaAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/cancelar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelarAsync(Guid id, CancellationToken cancellationToken)
    {
        var indicacao = await indicacaoService.ObterPorIdAsync(id, cancellationToken);

        if (!await IsAuthorizedAsync(indicacao, AuthorizationPolicies.IndicacaoOwnerOrAdmin))
            return Forbid();

        await indicacaoService.CancelarAsync(id, cancellationToken);
        return NoContent();
    }
}

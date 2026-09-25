using API.Security;
using Application.DTOs.Precificacao;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/precos-vistoria")]
[Authorize(Policy = AuthorizationPolicies.Administrador)]
public sealed class PrecosVistoriaController(IPrecificacaoService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken token) => Ok(await service.ListarAtivosAsync(token));
    [HttpGet("por-tipo/{tipoId:guid}/historico")]
    public async Task<IActionResult> Historico(Guid tipoId, CancellationToken token) => Ok(await service.HistoricoAsync(tipoId, token));
    [HttpPost("por-tipo/{tipoId:guid}")]
    public async Task<IActionResult> Publicar(Guid tipoId, PublicarPrecoDto dto, CancellationToken token) =>
        Created($"/api/precos-vistoria/por-tipo/{tipoId}/historico", await service.PublicarAsync(tipoId, dto, token));
    [HttpPatch("por-tipo/{tipoId:guid}/{precoId:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid tipoId, Guid precoId, CancellationToken token)
    { await service.DesativarPrecoAsync(tipoId, precoId, token); return NoContent(); }
    [HttpPost("simular")]
    public async Task<IActionResult> Simular(SimularVistoriaDto dto, CancellationToken token) => Ok(await service.SimularAsync(dto, token));
}

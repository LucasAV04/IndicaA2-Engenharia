using API.Security;
using Application.DTOs.Precificacao;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/tipos-planta")]
[Authorize(Policy = AuthorizationPolicies.Administrador)]
public sealed class TiposPlantaController(IPrecificacaoService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken token) => Ok(await service.ListarTiposAsync(token));
    [HttpPost]
    public async Task<IActionResult> Criar(NomeTipoPlantaDto dto, CancellationToken token) =>
        Created("/api/tipos-planta", new { Id = await service.CriarTipoAsync(dto, token) });
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Renomear(Guid id, NomeTipoPlantaDto dto, CancellationToken token)
    { await service.RenomearTipoAsync(id, dto, token); return NoContent(); }
    [HttpPatch("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken token)
    { await service.DesativarTipoAsync(id, token); return NoContent(); }
}

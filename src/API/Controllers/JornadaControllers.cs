using API.Security;
using Application.Jornada;
using Application.DTOs.DadosPix;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

[ApiController, AllowAnonymous, Route("api/public/indicacoes"), EnableRateLimiting("referral-link")]
[RequestSizeLimit(4096)]
public sealed class IndicacaoPublicaController(IJornadaPublicaStore store,TimeProvider clock) : ControllerBase
{
    [HttpGet("codigos/{codigo}")]
    public async Task<IActionResult> Codigo(string codigo,CancellationToken ct)
    {
        var utilizavel=await store.CodigoUtilizavelAsync(JornadaValidacao.Codigo(codigo),ct);
        return utilizavel ? Ok(new { Utilizavel=true }) : NotFound(new { Mensagem="Link indisponível." });
    }
    [HttpPost]
    public async Task<ActionResult<ProtocoloIndicacao>> Captar(IndicacaoPublicaRequest request,[FromHeader(Name="Idempotency-Key")] string chave,CancellationToken ct)
    {
        request=JornadaValidacao.Normalizar(request); JornadaValidacao.HashChave(chave);
        return Ok(await store.CaptarAsync(request,chave,clock.GetUtcNow().UtcDateTime,ct));
    }
}

[ApiController, Authorize, Route("api/minha-conta")]
public sealed class MinhaContaController(IJornadaConsultaStore store,ICurrentUser current,IConfiguration config) : ControllerBase
{
    public sealed class DadosPixEntrada
    {
        public Domain.Enums.TipoChavePix TipoChavePix { get; set; }
        public string ChavePix { get; set; }=string.Empty;
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string,System.Text.Json.JsonElement>? CamposExtras { get; set; }
    }
    private Guid Usuario()=>current.UserId ?? throw new UnauthorizedAccessException();
    [HttpGet]
    public async Task<ActionResult<PerfilPortal>> Perfil(CancellationToken ct)
    {
        var perfil=await store.PerfilAsync(Usuario(),ct);
        return Ok(new PerfilPortal(perfil.Nome,perfil.Codigo,JornadaValidacao.Link(config["PublicWeb:BaseUrl"],perfil.Codigo)));
    }
    [HttpGet("indicacoes")]
    public Task<IReadOnlyList<IndicacaoPortal>> Indicacoes(CancellationToken ct)=>store.IndicacoesAsync(Usuario(),ct);
    [HttpGet("cashbacks")]
    public Task<IReadOnlyList<CashbackPortal>> Cashbacks(CancellationToken ct)=>store.CashbacksAsync(Usuario(),ct);
    [HttpGet("dados-pix")]
    public async Task<IActionResult> Dados([FromServices] IDadosPixService dados,CancellationToken ct)
    {
        var dto=await dados.ObterPorUsuarioIdAsync(Usuario(),ct);
        return dto is null?NoContent():Ok(new { dto.TipoChavePix,ChaveMascarada="••••••••" });
    }
    [HttpPut("dados-pix")]
    public async Task<IActionResult> SalvarDados([FromServices] IDadosPixService dados,DadosPixEntrada dto,CancellationToken ct)
    {
        if(dto.CamposExtras?.Count>0) return NotFound();
        var result=await dados.CadastrarOuAtualizarAsync(Usuario(),new DadosPixDto {TipoChavePix=dto.TipoChavePix,ChavePix=dto.ChavePix},ct);
        return Ok(new { result.TipoChavePix,ChaveMascarada="••••••••" });
    }
}

[ApiController, Authorize, Route("api/notificacoes")]
public sealed class NotificacoesController(IJornadaConsultaStore store,ICurrentUser current,TimeProvider clock) : ControllerBase
{
    private Guid Usuario()=>current.UserId ?? throw new UnauthorizedAccessException();
    [HttpGet]
    public Task<IReadOnlyList<NotificacaoResponse>> Listar(CancellationToken ct)=>store.NotificacoesAsync(Usuario(),false,ct);
    [HttpGet("nao-lidas")]
    public async Task<IActionResult> Contar(CancellationToken ct)=>Ok(new { Quantidade=await store.NaoLidasAsync(Usuario(),false,ct) });
    [HttpPatch("{id:guid}/lida")]
    public async Task<IActionResult> Ler(Guid id,CancellationToken ct)=>await store.LerAsync(Usuario(),false,id,clock.GetUtcNow().UtcDateTime,ct)?NoContent():NotFound();
    [HttpPatch("lidas")]
    public async Task<IActionResult> LerTodas(CancellationToken ct) { await store.LerAsync(Usuario(),false,null,clock.GetUtcNow().UtcDateTime,ct); return NoContent(); }
}

[ApiController, Authorize(Policy=AuthorizationPolicies.Administrador), Route("api/admin/jornada")]
public sealed class JornadaAdminController(IJornadaConsultaStore store,ICurrentUser current,TimeProvider clock,IConfiguration config) : ControllerBase
{
    [HttpGet("indicadores")]
    public Task<IndicadoresJornada> Indicadores(CancellationToken ct)=>store.IndicadoresAsync(ct);
    [HttpGet("origens")]
    public Task<IReadOnlyList<OrigemIndicacaoResponse>> Origens(CancellationToken ct)=>store.OrigensAsync(ct);
    [HttpGet("notificacoes")]
    public Task<IReadOnlyList<NotificacaoResponse>> Alertas(CancellationToken ct)=>store.NotificacoesAsync(current.UserId!.Value,true,ct);
    [HttpPatch("notificacoes/{id:guid}/lida")]
    public async Task<IActionResult> Ler(Guid id,CancellationToken ct)=>await store.LerAsync(current.UserId!.Value,true,id,clock.GetUtcNow().UtcDateTime,ct)?NoContent():NotFound();
    [HttpPatch("notificacoes/lidas")]
    public async Task<IActionResult> Todas(CancellationToken ct) { await store.LerAsync(current.UserId!.Value,true,null,clock.GetUtcNow().UtcDateTime,ct); return NoContent(); }
    [HttpGet("usuarios/{id:guid}/link")]
    public async Task<IActionResult> Link(Guid id,CancellationToken ct) { var p=await store.PerfilAsync(id,ct); return Ok(new { Link=JornadaValidacao.Link(config["PublicWeb:BaseUrl"],p.Codigo) }); }
}

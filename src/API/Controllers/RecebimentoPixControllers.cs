using System.Globalization;
using System.Text.Json;
using API.Security;
using Application.Recebimentos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

[ApiController, Route("api/cobrancas-pix-vistoria"), Authorize(Policy=AuthorizationPolicies.Administrador)]
public sealed class CobrancasPixVistoriaController(ICobrancaPixVistoriaService service, ICobrancaPixVistoriaStore store,
    ICobrancaPixVistoriaProvider provider, RecebimentoPixOptions options) : ControllerBase
{
    [HttpPost("por-pagamento/{id:guid}")]
    public async Task<IActionResult> Gerar(Guid id,CancellationToken ct) => Ok(await service.GerarAsync(id,ct));
    [HttpGet] public async Task<IActionResult> Listar(CancellationToken ct) => Ok(await store.ListarAsync(ct));
    [HttpGet("indicadores")] public async Task<IActionResult> Indicadores(CancellationToken ct) => Ok(await store.IndicadoresAsync(ct));
    [HttpGet("{id:guid}/auditoria")] public async Task<IActionResult> Auditoria(Guid id,CancellationToken ct) => Ok(await store.AuditoriaAsync(id,ct));
    [HttpPost("{id:guid}/link")] public async Task<IActionResult> Link(Guid id,CancellationToken ct)
    { Response.Headers.CacheControl="no-store"; return Ok(await service.RotacionarLinkAsync(id,ct)); }
    [HttpPut("webhook")] public async Task<IActionResult> Configurar(CancellationToken ct)
    { options.ExigirHabilitado(); return Ok(new { configurado=await provider.ConfigurarWebhookAsync(ct) }); }
    [HttpGet("webhook")] public async Task<IActionResult> Webhook(CancellationToken ct)
    { options.ExigirHabilitado(); return Ok(new { configurado=await provider.ConsultarWebhookAsync(ct) }); }
}

[ApiController, Route("api/public/cobranca-pix-vistoria"), AllowAnonymous, EnableRateLimiting("payment-link")]
public sealed class CobrancaPixPublicaController(ICobrancaPixVistoriaService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Obter(CancellationToken ct)
    {
        Response.Headers.CacheControl="no-store";
        Response.Headers["Referrer-Policy"]="no-referrer";
        var auth=Request.Headers.Authorization.ToString();
        const string prefix="PaymentLink ";
        if (!auth.StartsWith(prefix,StringComparison.Ordinal) || auth.Length!=prefix.Length+64) return NotFound();
        var response=await service.ObterPublicaAsync(auth[prefix.Length..],ct);
        return response is null ? NotFound() : Ok(response);
    }
}

[ApiController, Route("api/webhooks/efi"), AllowAnonymous]
public sealed class RecebimentoPixWebhookController(IRecebimentoPixWebhookStore store,TimeProvider clock) : ControllerBase
{
    // A Efí verifica a URL base antes de acrescentar /pix aos eventos.
    [HttpPost] public IActionResult Verificar() => Ok();
    [HttpPost("pix"), RequestSizeLimit(65536)]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        if(Request.ContentLength>65536) return StatusCode(413);
        var eventos=new List<EventoPix>();
        try
        {
            using var bytes=new MemoryStream(); var buffer=new byte[4096]; int count;
            while((count=await Request.Body.ReadAsync(buffer,ct))>0)
            { if(bytes.Length+count>65536) return StatusCode(413); bytes.Write(buffer,0,count); }
            using var json=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions { MaxDepth=16 });
            var itens=json.RootElement.GetProperty("pix");
            if(itens.ValueKind!=JsonValueKind.Array || itens.GetArrayLength() is <1 or >100) return BadRequest();
            foreach(var i in itens.EnumerateArray())
            {
                var horarioTexto=i.GetProperty("horario").GetString();
                if (horarioTexto is null) return BadRequest();
                if(horarioTexto.Length>40 || (!horarioTexto.EndsWith('Z') && !System.Text.RegularExpressions.Regex.IsMatch(horarioTexto,@"[+-]\d{2}:\d{2}$"))) return BadRequest();
                var e=new EventoPix(i.GetProperty("endToEndId").GetString()!,i.GetProperty("txid").GetString()!,
                    decimal.Parse(i.GetProperty("valor").GetString()!,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(horarioTexto,CultureInfo.InvariantCulture).UtcDateTime);
                if(!RecebimentoPixValidacao.EventoValido(e,clock.GetUtcNow().UtcDateTime)) return BadRequest();
                eventos.Add(e);
            }
        }
        catch(Exception e) when(e is JsonException or KeyNotFoundException or FormatException or OverflowException or ArgumentException or InvalidOperationException)
        { return BadRequest(); }
        // Falhas de persistência não são confundidas com erro de payload nem
        // respondidas com 200: a Efí deve poder entregar novamente o evento.
        await store.PersistirAsync(eventos,ct);
        return Ok();
    }
}

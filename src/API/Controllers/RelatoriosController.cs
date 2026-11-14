using System.Text;
using API.Security;
using Application.Relatorios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/admin/relatorios")]
[Authorize(Policy = AuthorizationPolicies.Administrador)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RelatoriosController(IRelatorioOperacionalStore store, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Consultar([FromQuery] Consulta query, CancellationToken ct)
    {
        var filtro = CriarFiltro(query);
        return filtro.Valido ? Ok(await store.ConsultarAsync(filtro, false, ct)) : BadRequest();
    }

    [HttpGet("csv")]
    public async Task<IActionResult> Exportar([FromQuery] Consulta query, CancellationToken ct)
    {
        var filtro = CriarFiltro(query);
        if (!filtro.Valido)
            return BadRequest();

        var result = await store.ConsultarAsync(filtro, true, ct);
        if (result.Total > FiltroRelatorio.LimiteExportacao)
            return BadRequest(new { mensagem = "Reduza o período para exportar até 5000 registros." });

        var csv = new StringBuilder("id,dataUtc,status,valor\r\n");
        foreach (var linha in result.Linhas)
            csv.Append(CsvRelatorio.Linha(linha)).Append("\r\n");
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "relatorio-indicaa2.csv");
    }

    private FiltroRelatorio CriarFiltro(Consulta query)
    {
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var fim = query.Fim ?? hoje;
        var inicio = query.Inicio ?? (fim.DayNumber >= 29 ? fim.AddDays(-29) : DateOnly.MinValue);
        return new FiltroRelatorio(query.Tipo, inicio, fim, query.Status, query.Pagina, query.Tamanho);
    }

    public sealed record Consulta(TipoRelatorio Tipo, DateOnly? Inicio = null, DateOnly? Fim = null,
        int? Status = null, int Pagina = 1, int Tamanho = 30);
}

using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using API.Security;
using Application.Contas;
using Infrastructure.Repositories;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("seguranca-conta")]
[RequestSizeLimit(4096)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContaSegurancaController(ContaMySqlStore store, IServiceProvider services,
    ILogger<ContaSegurancaController> logger) : ControllerBase
{
    private const string TokenHeader = "X-Conta-Token";

    internal static bool Protege(PathString path) => path.StartsWithSegments("/api/auth/esqueci-senha")
        || path.StartsWithSegments("/api/auth/redefinir-senha")
        || path.StartsWithSegments("/api/auth/confirmar-email");

    [AllowAnonymous]
    [HttpPost("/api/auth/esqueci-senha")]
    public async Task<IActionResult> EsqueciSenha(Solicitacao request, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var protector = services.GetService<TokenContaProtector>();
        try
        {
            if (protector is not null)
                await store.SolicitarAsync(request.Email, null, FinalidadeTokenConta.RedefinicaoSenha, protector, ct);
        }
        catch (MySqlConnector.MySqlException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Solicitação de conta indisponível ({Tipo})", ex.GetType().Name);
        }
        var remaining = TimeSpan.FromMilliseconds(250) - started.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);

        return Accepted(new { mensagem = "Se o endereço puder receber instruções, enviaremos um e-mail." });
    }

    [AllowAnonymous]
    [HttpPost("/api/auth/redefinir-senha")]
    public async Task<IActionResult> RedefinirSenha(NovaSenha request,
        [FromHeader(Name = TokenHeader)] string? token, CancellationToken ct)
        => Request.Query.Count == 0 && await store.ConsumirAsync(token, FinalidadeTokenConta.RedefinicaoSenha, request.Senha, ct)
            ? NoContent() : TokenInvalido();

    [AllowAnonymous]
    [HttpPost("/api/auth/confirmar-email")]
    public async Task<IActionResult> ConfirmarEmail([FromHeader(Name = TokenHeader)] string? token, CancellationToken ct)
        => Request.Query.Count == 0 && HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>()?.CanHaveBody != true
            && await store.ConsumirAsync(token, FinalidadeTokenConta.ConfirmacaoEmail, null, ct)
            ? NoContent() : TokenInvalido();

    [HttpPost("/api/minha-conta/email/confirmacao")]
    public async Task<IActionResult> SolicitarConfirmacao(CancellationToken ct)
    {
        if (!CurrentUserClaims.TryGetUserId(User, out var id))
            return Unauthorized();

        var protector = services.GetService<TokenContaProtector>();
        if (protector is not null)
            await store.SolicitarAsync(null, id, FinalidadeTokenConta.ConfirmacaoEmail, protector, ct);
        return Accepted();
    }

    [HttpGet("/api/minha-conta/seguranca")]
    public async Task<IActionResult> ObterSeguranca([FromServices] Domain.Interfaces.IUsuarioRepository usuarios, CancellationToken ct)
    {
        if (!CurrentUserClaims.TryGetUserId(User, out var id))
            return Unauthorized();
        var usuario = await usuarios.ObterPorIdAsync(id, ct);
        return usuario is null ? Unauthorized() : Ok(new
        {
            usuario.EmailConfirmado,
            EmailHabilitado = services.GetService<TokenContaProtector>() is not null
        });
    }

    [HttpPatch("/api/minha-conta/senha")]
    public async Task<IActionResult> AlterarSenha(TrocaSenha request, CancellationToken ct)
    {
        if (!CurrentUserClaims.TryGetUserId(User, out var id)
            || !long.TryParse(User.FindFirst("cv")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            return Unauthorized();

        if (!SegurancaConta.NovaSenhaValida(request.NovaSenha) || string.IsNullOrWhiteSpace(request.SenhaAtual)
            || Encoding.UTF8.GetByteCount(request.SenhaAtual) > 72)
            return BadRequest(new { mensagem = "Verifique as senhas informadas." });

        return await store.AlterarSenhaAsync(id, version, request.SenhaAtual, request.NovaSenha!, ct)
            ? NoContent() : BadRequest(new { mensagem = "Não foi possível alterar a senha." });
    }

    private BadRequestObjectResult TokenInvalido() =>
        BadRequest(new { mensagem = "Link inválido ou expirado. Solicite outro e-mail." });

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Solicitacao(string? Email);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record NovaSenha(string? Senha);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrocaSenha(string? SenhaAtual, string? NovaSenha);
}

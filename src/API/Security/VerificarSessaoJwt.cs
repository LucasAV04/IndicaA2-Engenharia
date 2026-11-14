using System.Globalization;
using Domain.Enums;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MySqlConnector;

namespace API.Security;

internal sealed class VerificarSessaoJwt(IUsuarioRepository usuarios) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var role = principal?.FindFirst("role")?.Value;
        if (!CurrentUserClaims.TryGetUserId(principal, out var id)
            || !long.TryParse(principal?.FindFirst("cv")?.Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var versao) || versao < 1)
        {
            context.Fail("Sessão inválida.");
            return;
        }

        try
        {
            var usuario = await usuarios.ObterPorIdAsync(id, context.HttpContext.RequestAborted);
            if (usuario is null || usuario.Status != StatusUsuario.Ativo
                || usuario.VersaoCredenciais != versao || usuario.TipoUsuario.ToString() != role)
                context.Fail("Sessão inválida.");
        }
        catch (MySqlException)
        {
            context.Fail("Sessão indisponível.");
        }
    }
}

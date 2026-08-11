using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Application.Interfaces.Security;
using Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Security;

public sealed class JwtAccessTokenGenerator(IOptions<JwtOptions> options) : IAccessTokenGenerator
{
    private readonly JwtOptions _options = options.Value;
    public AccessTokenResult Generate(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        _options.Validate();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, usuario.Email), new Claim(ClaimTypes.Name, usuario.Nome), new Claim(ClaimTypes.Role, usuario.TipoUsuario.ToString()) };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, expires: expiresAtUtc, signingCredentials: credentials);
        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}

using Domain.Enums;

namespace Application.DTOs.Auth;

public sealed class LoginResponseDto
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public Guid UsuarioId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public TipoUsuario TipoUsuario { get; init; }
}

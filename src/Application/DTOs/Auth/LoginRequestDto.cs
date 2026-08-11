namespace Application.DTOs.Auth;

public sealed class LoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
}

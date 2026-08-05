using Domain.Enums;

namespace Application.DTOs.Usuario
{
    public sealed class CreateUsuarioDto
    {
        public string Nome { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Senha { get; set; } = string.Empty;

        public string? Telefone { get; set; }
    }
}

using Domain.Enums;

namespace Application.DTOs.Usuario
{
    public sealed class UsuarioResponseDto
    {
        public Guid Id { get; set; }

        public string Nome { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? Telefone { get; set; }

        public string? CodigoIndicacao { get; set; }

        public StatusUsuario Status { get; set; }

        public TipoUsuario TipoUsuario { get; set; }

        public bool EmailConfirmado { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}

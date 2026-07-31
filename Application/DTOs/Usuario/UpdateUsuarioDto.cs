namespace Application.DTOs.Usuario
{
    public sealed class UpdateUsuarioDto
    {
        public Guid Id { get; set; }

        public string Nome { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? Telefone { get; set; }
    }
}

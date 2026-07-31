namespace Application.DTOs.Usuario
{
    public sealed class AlterarSenhaUsuarioDto
    {
        public Guid UsuarioId { get; set; }

        public string SenhaAtual { get; set; } = string.Empty;

        public string NovaSenha { get; set; } = string.Empty;

        public string ConfirmarSenha { get; set; } = string.Empty;
    }
}

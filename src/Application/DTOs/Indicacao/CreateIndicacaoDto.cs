namespace Application.DTOs.Indicacao
{
    public sealed class CreateIndicacaoDto
    {
        public Guid UsuarioIndicadorId { get; set; }

        public string NomeIndicada { get; set; } = string.Empty;

        public string TelefoneIndicada { get; set; } = string.Empty;

        public string CodigoIndicacaoUsado { get; set; } = string.Empty;
    }
}

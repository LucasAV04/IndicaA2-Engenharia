namespace Application.DTOs.Indicacao
{
    public sealed class CreateIndicacaoPorCodigoDto
    {
        public string CodigoIndicacao { get; set; } = string.Empty;

        public string NomeIndicada { get; set; } = string.Empty;

        public string TelefoneIndicada { get; set; } = string.Empty;
    }
}

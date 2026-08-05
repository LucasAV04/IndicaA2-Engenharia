using Domain.Enums;

namespace Application.DTOs.Indicacao
{
    public sealed class IndicacaoResponseDto
    {
        public Guid Id { get; set; }

        public Guid UsuarioIndicadorId { get; set; }

        public Guid? UsuarioIndicadoId { get; set; }

        public string NomeIndicada { get; set; } = string.Empty;

        public string TelefoneIndicada { get; set; } = string.Empty;

        public string CodigoIndicacaoUsado { get; set; } = string.Empty;

        public Guid? VistoriaId { get; set; }

        public StatusIndicacao Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}

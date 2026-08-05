using Application.DTOs.Indicacao;
using Domain.Entities;

namespace Application.Mapping
{
    public static class IndicacaoMapper
    {
        public static IndicacaoResponseDto ToResponseDto(this Indicacao indicacao)
        {
            ArgumentNullException.ThrowIfNull(indicacao);

            return new IndicacaoResponseDto
            {
                Id = indicacao.Id,
                UsuarioIndicadorId = indicacao.UsuarioIndicadorId,
                UsuarioIndicadoId = indicacao.UsuarioIndicadoId,
                NomeIndicada = indicacao.NomeIndicada,
                TelefoneIndicada = indicacao.TelefoneIndicada,
                CodigoIndicacaoUsado = indicacao.CodigoIndicacaoUsado,
                VistoriaId = indicacao.VistoriaId,
                Status = indicacao.Status,
                CreatedAt = indicacao.CreatedAt,
                UpdatedAt = indicacao.UpdatedAt
            };
        }

        public static IReadOnlyCollection<IndicacaoResponseDto> ToResponseDto(
            this IEnumerable<Indicacao> indicacoes)
        {
            ArgumentNullException.ThrowIfNull(indicacoes);

            return indicacoes
                .Select(indicacao => indicacao.ToResponseDto())
                .ToList()
                .AsReadOnly();
        }
    }
}

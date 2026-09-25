using Application.DTOs.Precificacao;
using Domain.Entities;
using Domain.Services;

namespace Application.Mapping;

public static class PrecificacaoMapper
{
    public static PrecoVistoriaResponseDto ToResponseDto(this PrecoVistoria p) =>
        new(p.Id, p.TipoPlantaId, p.NomeTipoPlanta, p.PrecoM2, p.Modalidade, p.Acrescimo, p.Versao, p.Ativo, p.CreatedAt, p.UpdatedAt, p.DesativadoEm);
    public static CalculoVistoriaResponseDto ToResponseDto(this CalculoVistoria c, bool simulacao) =>
        new(c.PrecoId, c.Versao, c.TipoPlantaId, c.NomeTipoPlanta, c.AreaM2, c.Pacote,
            c.PrecoM2, c.Modalidade, c.Acrescimo, c.ValorBase, c.ValorFinal, c.CalculadoEmUtc, simulacao);
}

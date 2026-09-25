using System.Text.Json.Serialization;
using Domain.Enums;

namespace Application.DTOs.Precificacao;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NomeTipoPlantaDto(string Nome);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicarPrecoDto(decimal PrecoM2, ModalidadeAcrescimo Modalidade, decimal Acrescimo, int VersaoEsperada);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SimularVistoriaDto(Guid TipoPlantaId, decimal AreaM2, PacoteVistoria Pacote);

public sealed record TipoPlantaResponseDto(Guid Id, string Nome, bool Ativo, bool PossuiPrecoAtivo, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record PrecoVistoriaResponseDto(Guid Id, Guid TipoPlantaId, string NomeTipoPlanta, decimal PrecoM2,
    ModalidadeAcrescimo Modalidade, decimal Acrescimo, int Versao, bool Ativo, DateTime CreatedAt, DateTime UpdatedAt, DateTime? DesativadoEm);
public sealed record CalculoVistoriaResponseDto(Guid PrecoId, int Versao, Guid TipoPlantaId, string NomeTipoPlanta,
    decimal AreaM2, PacoteVistoria Pacote, decimal PrecoM2, ModalidadeAcrescimo Modalidade, decimal Acrescimo,
    decimal ValorBase, decimal ValorFinal, DateTime CalculadoEmUtc, bool Simulacao);

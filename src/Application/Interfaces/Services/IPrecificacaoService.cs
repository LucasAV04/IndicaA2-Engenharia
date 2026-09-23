using Application.DTOs.Precificacao;

namespace Application.Interfaces.Services;

public interface IPrecificacaoService
{
    Task<IReadOnlyCollection<TipoPlantaResponseDto>> ListarTiposAsync(CancellationToken token = default);
    Task<Guid> CriarTipoAsync(NomeTipoPlantaDto dto, CancellationToken token = default);
    Task RenomearTipoAsync(Guid id, NomeTipoPlantaDto dto, CancellationToken token = default);
    Task DesativarTipoAsync(Guid id, CancellationToken token = default);
    Task<IReadOnlyCollection<PrecoVistoriaResponseDto>> ListarAtivosAsync(CancellationToken token = default);
    Task<IReadOnlyCollection<PrecoVistoriaResponseDto>> HistoricoAsync(Guid tipoId, CancellationToken token = default);
    Task<PrecoVistoriaResponseDto> PublicarAsync(Guid tipoId, PublicarPrecoDto dto, CancellationToken token = default);
    Task DesativarPrecoAsync(Guid tipoId, Guid precoId, CancellationToken token = default);
    Task<CalculoVistoriaResponseDto> SimularAsync(SimularVistoriaDto dto, CancellationToken token = default);
}

using Application.DTOs.Precificacao;
using Domain.Entities;
using Domain.Enums;
using Domain.Services;

namespace Application.Interfaces.Stores;

public interface IPrecificacaoStore
{
    Task<IReadOnlyCollection<TipoPlantaResponseDto>> ListarTiposAsync(CancellationToken token);
    Task CriarTipoAsync(TipoPlanta tipo, CancellationToken token);
    Task RenomearTipoAsync(Guid id, string nome, DateTime instante, CancellationToken token);
    Task DesativarTipoAsync(Guid id, DateTime instante, CancellationToken token);
    Task<IReadOnlyCollection<PrecoVistoria>> ListarAtivosAsync(CancellationToken token);
    Task<IReadOnlyCollection<PrecoVistoria>> HistoricoAsync(Guid tipoId, CancellationToken token);
    Task<PrecoVistoria> PublicarAsync(Guid tipoId, PublicarPrecoDto entrada, DateTime instante, CancellationToken token);
    Task DesativarPrecoAsync(Guid tipoId, Guid precoId, DateTime instante, CancellationToken token);
    Task<CalculoVistoria> SimularAsync(SimularVistoriaDto entrada, DateTime instante, CancellationToken token);
    Task<Vistoria> CriarVistoriaAsync(Guid usuarioId, Guid tipoId, decimal area, PacoteVistoria pacote,
        DateTime dataAgendada, DateTime instante, CancellationToken token);
}

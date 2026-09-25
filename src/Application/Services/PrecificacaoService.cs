using Application.DTOs.Precificacao;
using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Application.Mapping;
using Domain.Entities;

namespace Application.Services;

public sealed class PrecificacaoService(IPrecificacaoStore store, TimeProvider clock) : IPrecificacaoService
{
    public Task<IReadOnlyCollection<TipoPlantaResponseDto>> ListarTiposAsync(CancellationToken token = default) => store.ListarTiposAsync(token);
    public async Task<Guid> CriarTipoAsync(NomeTipoPlantaDto dto, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var tipo = new TipoPlanta(dto.Nome, clock.GetUtcNow().UtcDateTime);
        await store.CriarTipoAsync(tipo, token);
        return tipo.Id;
    }
    public Task RenomearTipoAsync(Guid id, NomeTipoPlantaDto dto, CancellationToken token = default) =>
        store.RenomearTipoAsync(id, TipoPlanta.ValidarNome(dto.Nome), clock.GetUtcNow().UtcDateTime, token);
    public Task DesativarTipoAsync(Guid id, CancellationToken token = default) => store.DesativarTipoAsync(id, clock.GetUtcNow().UtcDateTime, token);
    public async Task<IReadOnlyCollection<PrecoVistoriaResponseDto>> ListarAtivosAsync(CancellationToken token = default) =>
        (await store.ListarAtivosAsync(token)).Select(p => p.ToResponseDto()).ToArray();
    public async Task<IReadOnlyCollection<PrecoVistoriaResponseDto>> HistoricoAsync(Guid tipoId, CancellationToken token = default) =>
        (await store.HistoricoAsync(tipoId, token)).Select(p => p.ToResponseDto()).ToArray();
    public async Task<PrecoVistoriaResponseDto> PublicarAsync(Guid tipoId, PublicarPrecoDto dto, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        PrecoVistoria.ValidarValores(dto.PrecoM2, dto.Modalidade, dto.Acrescimo);
        if (dto.VersaoEsperada < 0) throw new ArgumentException("Versão esperada inválida.");
        return (await store.PublicarAsync(tipoId, dto, clock.GetUtcNow().UtcDateTime, token)).ToResponseDto();
    }
    public Task DesativarPrecoAsync(Guid tipoId, Guid precoId, CancellationToken token = default) =>
        store.DesativarPrecoAsync(tipoId, precoId, clock.GetUtcNow().UtcDateTime, token);
    public async Task<CalculoVistoriaResponseDto> SimularAsync(SimularVistoriaDto dto, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (dto.TipoPlantaId == Guid.Empty) throw new ArgumentException("Tipo de planta obrigatório.");
        Domain.Services.MotorPrecificacaoVistoria.ValidarEntrada(dto.AreaM2, dto.Pacote);
        return (await store.SimularAsync(dto, clock.GetUtcNow().UtcDateTime, token)).ToResponseDto(true);
    }
}

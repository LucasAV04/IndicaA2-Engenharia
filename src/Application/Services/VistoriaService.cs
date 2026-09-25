using Application.DTOs.Vistoria;
using Application.Interfaces.Services;
using Application.Mapping;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Exceptions.Usuario;
using Domain.Interfaces;

namespace Application.Services;

public sealed class VistoriaService : IVistoriaService
{
    private readonly IVistoriaRepository _vistoriaRepository;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly Application.Interfaces.Stores.IPrecificacaoStore _precificacao;
    private readonly TimeProvider _clock;

    public VistoriaService(
        IVistoriaRepository vistoriaRepository,
        IUsuarioRepository usuarioRepository,
        Application.Interfaces.Stores.IPrecificacaoStore precificacao,
        TimeProvider clock)
    {
        _vistoriaRepository = vistoriaRepository;
        _usuarioRepository = usuarioRepository;
        _precificacao = precificacao;
        _clock = clock;
    }

    #region Consultas

    public async Task<VistoriaResponseDto> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        (await ObterVistoriaOuLancarExceptionAsync(id, cancellationToken)).ToResponseDto();

    public async Task<IReadOnlyCollection<VistoriaResponseDto>> ObterTodasAsync(
        CancellationToken cancellationToken = default) =>
        (await _vistoriaRepository.ObterTodasAsync(cancellationToken)).ToResponseDto();

    public async Task<IReadOnlyCollection<VistoriaResponseDto>> ObterPorUsuarioIdAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default) =>
        (await _vistoriaRepository.ObterPorUsuarioIdAsync(usuarioId, cancellationToken)).ToResponseDto();

    #endregion

    #region Comandos

    public async Task<VistoriaResponseDto> CriarAsync(
        CreateVistoriaDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        cancellationToken.ThrowIfCancellationRequested();
        if (dto.TipoPlantaId == Guid.Empty) throw new DomainException("Selecione um tipo de planta do catálogo.");
        Domain.Services.MotorPrecificacaoVistoria.ValidarEntrada(dto.AreaM2, dto.Pacote);
        if (await _usuarioRepository.ObterPorIdAsync(dto.UsuarioId, cancellationToken) is null)
            throw new UsuarioNaoEncontradoException();

        var vistoria = await _precificacao.CriarVistoriaAsync(dto.UsuarioId, dto.TipoPlantaId,
            dto.AreaM2, dto.Pacote, dto.DataAgendada, _clock.GetUtcNow().UtcDateTime, cancellationToken);

        return vistoria.ToResponseDto();
    }

    public async Task MarcarRealizadaAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var vistoria = await ObterVistoriaOuLancarExceptionAsync(id, cancellationToken);
        var statusAnterior = vistoria.Status;

        vistoria.MarcarRealizada();

        if (vistoria.Status != statusAnterior)
            await _vistoriaRepository.AtualizarAsync(vistoria, cancellationToken);
    }

    public async Task ConcluirAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var vistoria = await ObterVistoriaOuLancarExceptionAsync(id, cancellationToken);
        var statusAnterior = vistoria.Status;

        vistoria.Concluir();

        if (vistoria.Status != statusAnterior)
            await _vistoriaRepository.AtualizarAsync(vistoria, cancellationToken);
    }

    public async Task CancelarAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var vistoria = await ObterVistoriaOuLancarExceptionAsync(id, cancellationToken);
        var statusAnterior = vistoria.Status;

        vistoria.Cancelar();

        if (vistoria.Status != statusAnterior)
            await _vistoriaRepository.AtualizarAsync(vistoria, cancellationToken);
    }

    #endregion

    #region Métodos Privados

    private async Task<Vistoria> ObterVistoriaOuLancarExceptionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var vistoria = await _vistoriaRepository.ObterPorIdAsync(id, cancellationToken);
        return vistoria ?? throw new VistoriaNaoEncontradaException();
    }

    #endregion
}

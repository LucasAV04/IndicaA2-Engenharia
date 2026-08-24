using Application.DTOs.DadosPix;
using Application.Interfaces.Services;
using Application.Mapping;
using Domain.Entities;
using Domain.Exceptions.Usuario;
using Domain.Interfaces;

namespace Application.Services;

public sealed class DadosPixService : IDadosPixService
{
    private readonly IDadosPixRepository _dadosPixRepository;
    private readonly IUsuarioRepository _usuarioRepository;

    public DadosPixService(
        IDadosPixRepository dadosPixRepository,
        IUsuarioRepository usuarioRepository)
    {
        _dadosPixRepository = dadosPixRepository;
        _usuarioRepository = usuarioRepository;
    }

    #region Consultas

    public async Task<DadosPixResponseDto?> ObterPorUsuarioIdAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        await GarantirUsuarioExisteAsync(usuarioId);

        var dadosPix = await _dadosPixRepository.ObterPorUsuarioIdAsync(usuarioId, cancellationToken);
        return dadosPix?.ToResponseDto();
    }

    #endregion

    #region Comandos

    public async Task<DadosPixResponseDto> CadastrarOuAtualizarAsync(
        Guid usuarioId,
        DadosPixDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await GarantirUsuarioExisteAsync(usuarioId);

        var dadosPix = await _dadosPixRepository.ObterPorUsuarioIdAsync(usuarioId, cancellationToken);
        if (dadosPix is null)
        {
            dadosPix = new DadosPix(usuarioId, dto.TipoChavePix, dto.ChavePix);
            await _dadosPixRepository.AdicionarAsync(dadosPix, cancellationToken);
        }
        else
        {
            dadosPix.Atualizar(dto.TipoChavePix, dto.ChavePix);
            await _dadosPixRepository.AtualizarAsync(dadosPix, cancellationToken);
        }

        return dadosPix.ToResponseDto();
    }

    public async Task RemoverAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        await GarantirUsuarioExisteAsync(usuarioId);

        var dadosPix = await _dadosPixRepository.ObterPorUsuarioIdAsync(usuarioId, cancellationToken);
        if (dadosPix is not null)
            await _dadosPixRepository.RemoverAsync(dadosPix, cancellationToken);
    }

    #endregion

    #region Métodos Privados

    private async Task GarantirUsuarioExisteAsync(Guid usuarioId)
    {
        if (usuarioId == Guid.Empty || !await _usuarioRepository.ExistePorIdAsync(usuarioId))
            throw new UsuarioNaoEncontradoException();
    }

    #endregion
}

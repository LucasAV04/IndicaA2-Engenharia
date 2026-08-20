using Application.DTOs.PagamentoVistoria;
using Application.Interfaces.Services;
using Application.Mapping;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services;

public sealed class PagamentoVistoriaService : IPagamentoVistoriaService
{
    private readonly IPagamentoVistoriaRepository _pagamentoVistoriaRepository;
    private readonly IVistoriaRepository _vistoriaRepository;

    public PagamentoVistoriaService(
        IPagamentoVistoriaRepository pagamentoVistoriaRepository,
        IVistoriaRepository vistoriaRepository)
    {
        _pagamentoVistoriaRepository = pagamentoVistoriaRepository;
        _vistoriaRepository = vistoriaRepository;
    }

    #region Consultas

    public async Task<PagamentoVistoriaResponseDto> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        (await ObterPagamentoOuLancarExceptionAsync(id, cancellationToken)).ToResponseDto();

    public async Task<PagamentoVistoriaResponseDto> ObterPorVistoriaIdAsync(
        Guid vistoriaId,
        CancellationToken cancellationToken = default)
    {
        var pagamentoVistoria = await _pagamentoVistoriaRepository.ObterPorVistoriaIdAsync(
            vistoriaId,
            cancellationToken);

        return (pagamentoVistoria ?? throw new PagamentoVistoriaNaoEncontradoException()).ToResponseDto();
    }

    public async Task<IReadOnlyCollection<PagamentoVistoriaResponseDto>> ObterTodosAsync(
        CancellationToken cancellationToken = default) =>
        (await _pagamentoVistoriaRepository.ObterTodosAsync(cancellationToken)).ToResponseDto();

    #endregion

    #region Comandos

    public async Task<PagamentoVistoriaResponseDto> CriarAsync(
        CreatePagamentoVistoriaDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var vistoria = await _vistoriaRepository.ObterPorIdAsync(dto.VistoriaId, cancellationToken);

        if (vistoria is null)
            throw new VistoriaNaoEncontradaException();

        var pagamentoExistente = await _pagamentoVistoriaRepository.ObterPorVistoriaIdAsync(
            dto.VistoriaId,
            cancellationToken);

        if (pagamentoExistente is not null)
            throw new DomainException("Já existe um pagamento registrado para esta vistoria.");

        var pagamentoVistoria = new PagamentoVistoria(dto.VistoriaId, dto.Valor);

        await _pagamentoVistoriaRepository.AdicionarAsync(pagamentoVistoria, cancellationToken);

        return pagamentoVistoria.ToResponseDto();
    }

    public async Task ConfirmarAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var pagamentoVistoria = await ObterPagamentoOuLancarExceptionAsync(id, cancellationToken);
        var statusAnterior = pagamentoVistoria.Status;

        pagamentoVistoria.Confirmar();

        if (pagamentoVistoria.Status != statusAnterior)
            await _pagamentoVistoriaRepository.AtualizarAsync(pagamentoVistoria, cancellationToken);
    }

    public async Task CancelarAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var pagamentoVistoria = await ObterPagamentoOuLancarExceptionAsync(id, cancellationToken);
        var statusAnterior = pagamentoVistoria.Status;

        pagamentoVistoria.Cancelar();

        if (pagamentoVistoria.Status != statusAnterior)
            await _pagamentoVistoriaRepository.AtualizarAsync(pagamentoVistoria, cancellationToken);
    }

    #endregion

    #region Métodos Privados

    private async Task<PagamentoVistoria> ObterPagamentoOuLancarExceptionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var pagamentoVistoria = await _pagamentoVistoriaRepository.ObterPorIdAsync(id, cancellationToken);
        return pagamentoVistoria ?? throw new PagamentoVistoriaNaoEncontradoException();
    }

    #endregion
}

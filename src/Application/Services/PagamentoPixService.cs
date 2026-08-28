using Application.DTOs.PagamentoPix;
using Application.Interfaces.Services;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services;

public sealed class PagamentoPixService : IPagamentoPixService
{
    private readonly ICashbackRepository _cashbackRepository;
    private readonly IDadosPixRepository _dadosPixRepository;
    private readonly IPagamentoPixRepository _pagamentoPixRepository;

    public PagamentoPixService(
        ICashbackRepository cashbackRepository,
        IDadosPixRepository dadosPixRepository,
        IPagamentoPixRepository pagamentoPixRepository)
    {
        _cashbackRepository = cashbackRepository;
        _dadosPixRepository = dadosPixRepository;
        _pagamentoPixRepository = pagamentoPixRepository;
    }

    #region Consultas

    public async Task<PagamentoPixResponseDto> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        (await ObterPagamentoPixOuLancarExceptionAsync(id, cancellationToken)).ToResponseDto();

    public async Task<PagamentoPixResponseDto> ObterPorCashbackIdAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default)
    {
        var pagamentoPix = await _pagamentoPixRepository.ObterPorCashbackIdAsync(
            cashbackId,
            cancellationToken);

        return (pagamentoPix ?? throw new PagamentoPixNaoEncontradoException()).ToResponseDto();
    }

    public async Task<IReadOnlyCollection<PagamentoPixResponseDto>> ObterPorUsuarioBeneficiarioIdAsync(
        Guid usuarioBeneficiarioId,
        CancellationToken cancellationToken = default) =>
        (await _pagamentoPixRepository
            .ObterPorUsuarioBeneficiarioIdAsync(usuarioBeneficiarioId, cancellationToken))
        .ToResponseDto();

    #endregion

    #region Comandos

    public async Task<PagamentoPixResponseDto> CriarPorCashbackAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default)
    {
        if (cashbackId == Guid.Empty)
            throw new ArgumentException("O identificador do cashback é obrigatório.", nameof(cashbackId));

        var cashback = await _cashbackRepository.ObterPorIdAsync(cashbackId, cancellationToken)
            ?? throw new CashbackNaoEncontradoException();

        if (cashback.Status != StatusCashback.Disponivel)
            throw new CashbackNaoElegivelParaPagamentoPixException();

        var pagamentoPixExistente = await _pagamentoPixRepository.ObterPorCashbackIdAsync(
            cashbackId,
            cancellationToken);

        if (pagamentoPixExistente is not null)
            throw new PagamentoPixJaExisteException();

        var dadosPix = await _dadosPixRepository.ObterPorUsuarioIdAsync(
            cashback.UsuarioIndicadorId,
            cancellationToken) ?? throw new DadosPixNaoCadastradosException();

        var pagamentoPix = PagamentoPix.Criar(
            cashback.Id,
            cashback.UsuarioIndicadorId,
            cashback.Valor,
            dadosPix.TipoChavePix,
            dadosPix.ChavePix);

        await _pagamentoPixRepository.AdicionarAsync(pagamentoPix, cancellationToken);

        return pagamentoPix.ToResponseDto();
    }

    public async Task CancelarAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var pagamentoPix = await ObterPagamentoPixOuLancarExceptionAsync(id, cancellationToken);
        pagamentoPix.Cancelar();
        await _pagamentoPixRepository.AtualizarAsync(pagamentoPix, cancellationToken);
    }

    public async Task<bool> TentarIniciarProcessamentoAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        _ = await ObterPagamentoPixOuLancarExceptionAsync(id, cancellationToken);
        return await _pagamentoPixRepository.TentarIniciarProcessamentoAsync(id, cancellationToken);
    }

    #endregion

    #region Métodos Privados

    private async Task<PagamentoPix> ObterPagamentoPixOuLancarExceptionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var pagamentoPix = await _pagamentoPixRepository.ObterPorIdAsync(id, cancellationToken);
        return pagamentoPix ?? throw new PagamentoPixNaoEncontradoException();
    }

    #endregion
}

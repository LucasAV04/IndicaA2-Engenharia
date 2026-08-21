using Application.DTOs.Cashback;
using Application.Interfaces.Services;
using Application.Mapping;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services;

public sealed class CashbackService : ICashbackService
{
    private readonly ICashbackRepository _cashbackRepository;
    private readonly IIndicacaoRepository _indicacaoRepository;
    private readonly IPagamentoVistoriaRepository _pagamentoVistoriaRepository;

    public CashbackService(
        ICashbackRepository cashbackRepository,
        IIndicacaoRepository indicacaoRepository,
        IPagamentoVistoriaRepository pagamentoVistoriaRepository)
    {
        _cashbackRepository = cashbackRepository;
        _indicacaoRepository = indicacaoRepository;
        _pagamentoVistoriaRepository = pagamentoVistoriaRepository;
    }

    #region Consultas

    public async Task<CashbackResponseDto> ObterPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        (await ObterCashbackOuLancarExceptionAsync(id, cancellationToken)).ToResponseDto();

    public async Task<CashbackResponseDto> ObterPorPagamentoVistoriaIdAsync(
        Guid pagamentoVistoriaId,
        CancellationToken cancellationToken = default)
    {
        var cashback = await _cashbackRepository.ObterPorPagamentoVistoriaIdAsync(
            pagamentoVistoriaId,
            cancellationToken);

        return (cashback ?? throw new CashbackNaoEncontradoException()).ToResponseDto();
    }

    public async Task<IReadOnlyCollection<CashbackResponseDto>> ObterPorUsuarioIndicadorIdAsync(
        Guid usuarioIndicadorId,
        CancellationToken cancellationToken = default) =>
        (await _cashbackRepository
            .ObterPorUsuarioIndicadorIdAsync(usuarioIndicadorId, cancellationToken))
        .ToResponseDto();

    public async Task<IReadOnlyCollection<CashbackResponseDto>> ObterTodosAsync(
        CancellationToken cancellationToken = default) =>
        (await _cashbackRepository.ObterTodosAsync(cancellationToken)).ToResponseDto();

    #endregion

    #region Comandos

    public async Task<CashbackResponseDto> GerarPorPagamentoAsync(
        Guid pagamentoVistoriaId,
        CancellationToken cancellationToken = default)
    {
        if (pagamentoVistoriaId == Guid.Empty)
            throw new ArgumentException("O identificador do pagamento da vistoria é obrigatório.", nameof(pagamentoVistoriaId));

        var pagamentoVistoria = await _pagamentoVistoriaRepository.ObterPorIdAsync(
            pagamentoVistoriaId,
            cancellationToken) ?? throw new PagamentoVistoriaNaoEncontradoException();

        if (pagamentoVistoria.Status != StatusPagamentoVistoria.Confirmado)
        {
            throw new DomainException(
                "Apenas pagamentos de vistoria confirmados podem gerar cashback.");
        }

        var indicacao = await _indicacaoRepository.ObterPorVistoriaIdAsync(
            pagamentoVistoria.VistoriaId,
            cancellationToken);

        if (indicacao is null)
            throw new DomainException("Não existe indicação vinculada à vistoria deste pagamento.");
        if (indicacao.UsuarioIndicadorId == Guid.Empty)
            throw new DomainException("A indicação não possui usuário indicador válido.");

        var cashbackExistente = await _cashbackRepository.ObterPorPagamentoVistoriaIdAsync(
            pagamentoVistoriaId,
            cancellationToken);

        if (cashbackExistente is not null)
            throw new CashbackJaExisteException();

        var cashback = Cashback.Criar(
            indicacao.Id,
            pagamentoVistoria.Id,
            indicacao.UsuarioIndicadorId,
            pagamentoVistoria.Valor);

        await _cashbackRepository.AdicionarAsync(cashback, cancellationToken);

        return cashback.ToResponseDto();
    }

    public async Task AprovarAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default)
    {
        var cashback = await ObterCashbackOuLancarExceptionAsync(cashbackId, cancellationToken);
        var statusAnterior = cashback.Status;

        cashback.Aprovar();

        if (cashback.Status != statusAnterior)
            await _cashbackRepository.AtualizarAsync(cashback, cancellationToken);
    }

    public async Task CancelarAsync(
        Guid cashbackId,
        CancellationToken cancellationToken = default)
    {
        var cashback = await ObterCashbackOuLancarExceptionAsync(cashbackId, cancellationToken);
        var statusAnterior = cashback.Status;

        cashback.Cancelar();

        if (cashback.Status != statusAnterior)
            await _cashbackRepository.AtualizarAsync(cashback, cancellationToken);
    }

    #endregion

    #region Métodos Privados

    private async Task<Cashback> ObterCashbackOuLancarExceptionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var cashback = await _cashbackRepository.ObterPorIdAsync(id, cancellationToken);
        return cashback ?? throw new CashbackNaoEncontradoException();
    }

    #endregion
}

using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Application.Models;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services;

/// <summary>
/// Converte uma evidência conclusiva e já auditada no ciclo atual em estado financeiro interno.
/// Não cria nem altera operações de auditoria e não depende de provider Pix.
/// </summary>
public sealed class PagamentoPixAplicacaoResultadoService : IPagamentoPixAplicacaoResultadoService
{
    private readonly IPagamentoPixRepository _pagamentoPixRepository;
    private readonly ICashbackRepository _cashbackRepository;
    private readonly IPagamentoPixAplicacaoResultadoStore _aplicacaoResultadoStore;

    public PagamentoPixAplicacaoResultadoService(
        IPagamentoPixRepository pagamentoPixRepository,
        ICashbackRepository cashbackRepository,
        IPagamentoPixAplicacaoResultadoStore aplicacaoResultadoStore)
    {
        _pagamentoPixRepository = pagamentoPixRepository;
        _cashbackRepository = cashbackRepository;
        _aplicacaoResultadoStore = aplicacaoResultadoStore;
    }

    #region Comandos

    public async Task<ResultadoAplicacaoPagamentoPix> AplicarAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default)
    {
        if (pagamentoPixId == Guid.Empty)
            throw new ArgumentException("O identificador do Pagamento Pix é obrigatório.", nameof(pagamentoPixId));

        cancellationToken.ThrowIfCancellationRequested();
        var pagamentoPix = await ObterPagamentoPixOuLancarExceptionAsync(pagamentoPixId, cancellationToken);
        var cashback = await ObterCashbackOuLancarExceptionAsync(pagamentoPix.CashbackId, cancellationToken);
        ValidarConsistenciaFinanceira(pagamentoPix, cashback);

        var resultadoPersistencia = await _aplicacaoResultadoStore.AplicarAsync(
            pagamentoPixId,
            cancellationToken);

        return resultadoPersistencia.Status switch
        {
            StatusPersistenciaAplicacaoPagamentoPix.Aplicado =>
                ResultadoAplicacaoPagamentoPix.Aplicado(pagamentoPixId, ObterResultadoObrigatorio(resultadoPersistencia)),
            StatusPersistenciaAplicacaoPagamentoPix.JaAplicado =>
                ResultadoAplicacaoPagamentoPix.JaAplicado(pagamentoPixId, ObterResultadoObrigatorio(resultadoPersistencia)),
            StatusPersistenciaAplicacaoPagamentoPix.SemResultadoConclusivo =>
                ResultadoAplicacaoPagamentoPix.SemResultadoConclusivo(pagamentoPixId),
            StatusPersistenciaAplicacaoPagamentoPix.RequerReconciliacao =>
                ResultadoAplicacaoPagamentoPix.RequerReconciliacao(pagamentoPixId),
            _ => throw new InvalidOperationException("O resultado da persistência financeira é inválido.")
        };
    }

    #endregion

    #region Métodos Privados

    private async Task<PagamentoPix> ObterPagamentoPixOuLancarExceptionAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken) =>
        await _pagamentoPixRepository.ObterPorIdAsync(pagamentoPixId, cancellationToken)
        ?? throw new PagamentoPixNaoEncontradoException();

    private async Task<Cashback> ObterCashbackOuLancarExceptionAsync(
        Guid cashbackId,
        CancellationToken cancellationToken) =>
        await _cashbackRepository.ObterPorIdAsync(cashbackId, cancellationToken)
        ?? throw new CashbackNaoEncontradoException();

    private static void ValidarConsistenciaFinanceira(PagamentoPix pagamentoPix, Cashback cashback)
    {
        if (pagamentoPix.CashbackId != cashback.Id ||
            pagamentoPix.UsuarioBeneficiarioId != cashback.UsuarioIndicadorId ||
            pagamentoPix.Valor != cashback.Valor)
        {
            throw new InvalidOperationException(
                "Pagamento Pix e Cashback possuem snapshots financeiros incompatíveis e requerem intervenção técnica.");
        }
    }

    private static Domain.Enums.ResultadoOperacaoPagamentoPix ObterResultadoObrigatorio(
        ResultadoPersistenciaAplicacaoPagamentoPix resultadoPersistencia) =>
        resultadoPersistencia.ResultadoOperacao
        ?? throw new InvalidOperationException("A persistência não informou a evidência conclusiva aplicada.");

    #endregion
}

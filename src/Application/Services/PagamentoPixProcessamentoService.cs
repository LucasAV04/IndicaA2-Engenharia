using Application.Interfaces.Services;
using Application.Models;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services;

/// <summary>
/// Avança um Pagamento Pix por uma única etapa lógica, delegando as garantias
/// transacionais e a chamada externa aos serviços especializados existentes.
/// </summary>
public sealed class PagamentoPixProcessamentoService : IPagamentoPixProcessamentoService
{
    private readonly IPagamentoPixRepository _pagamentoPixRepository;
    private readonly IPagamentoPixEnvioService _envioService;
    private readonly IPagamentoPixReconciliacaoService _reconciliacaoService;
    private readonly IPagamentoPixAplicacaoResultadoService _aplicacaoResultadoService;

    public PagamentoPixProcessamentoService(
        IPagamentoPixRepository pagamentoPixRepository,
        IPagamentoPixEnvioService envioService,
        IPagamentoPixReconciliacaoService reconciliacaoService,
        IPagamentoPixAplicacaoResultadoService aplicacaoResultadoService)
    {
        _pagamentoPixRepository = pagamentoPixRepository;
        _envioService = envioService;
        _reconciliacaoService = reconciliacaoService;
        _aplicacaoResultadoService = aplicacaoResultadoService;
    }

    #region Comandos

    public async Task<ResultadoProcessamentoPagamentoPix> ProcessarAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default)
    {
        if (pagamentoPixId == Guid.Empty)
            throw new ArgumentException("O identificador do Pagamento Pix é obrigatório.", nameof(pagamentoPixId));

        cancellationToken.ThrowIfCancellationRequested();
        var pagamentoPix = await ObterPagamentoPixOuLancarExceptionAsync(pagamentoPixId, cancellationToken);

        return pagamentoPix.Status switch
        {
            StatusPagamentoPix.Pendente => await ProcessarPendenteAsync(pagamentoPixId, cancellationToken),
            StatusPagamentoPix.Processando => await ProcessarProcessandoAsync(pagamentoPixId, cancellationToken),
            StatusPagamentoPix.Falhou => ResultadoProcessamentoPagamentoPix.Criar(
                pagamentoPixId,
                StatusProcessamentoPagamentoPix.AguardandoPoliticaRetry),
            StatusPagamentoPix.Concluido or StatusPagamentoPix.FalhaDefinitiva => ResultadoProcessamentoPagamentoPix.Criar(
                pagamentoPixId,
                StatusProcessamentoPagamentoPix.Terminal),
            StatusPagamentoPix.Cancelado => ResultadoProcessamentoPagamentoPix.Criar(
                pagamentoPixId,
                StatusProcessamentoPagamentoPix.NaoAplicavel),
            _ => throw new InvalidOperationException("O status do Pagamento Pix é inválido para processamento.")
        };
    }

    #endregion

    #region Métodos Privados

    private async Task<ResultadoProcessamentoPagamentoPix> ProcessarPendenteAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        var envio = await _envioService.ProcessarEnvioAsync(pagamentoPixId, cancellationToken);
        if (!envio.EnvioExecutado)
        {
            return ResultadoProcessamentoPagamentoPix.Criar(
                pagamentoPixId,
                StatusProcessamentoPagamentoPix.EnvioEmAndamento);
        }

        if (!EhConclusivo(envio.ResultadoOperacao))
        {
            return ResultadoProcessamentoPagamentoPix.Criar(
                pagamentoPixId,
                StatusProcessamentoPagamentoPix.EnvioExecutadoAguardandoResultado);
        }

        return await AplicarUmaVezAsync(pagamentoPixId, cancellationToken);
    }

    private async Task<ResultadoProcessamentoPagamentoPix> ProcessarProcessandoAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        var aplicacao = await _aplicacaoResultadoService.AplicarAsync(pagamentoPixId, cancellationToken);
        if (aplicacao.Status is StatusAplicacaoPagamentoPix.Aplicado or StatusAplicacaoPagamentoPix.JaAplicado)
            return MapearAplicacao(pagamentoPixId, aplicacao.Status);

        var reconciliacao = await _reconciliacaoService.ReconciliarAsync(pagamentoPixId, cancellationToken);
        return reconciliacao.Status switch
        {
            StatusReconciliacaoPagamentoPix.ConsultaEmAndamento => ResultadoProcessamentoPagamentoPix.Criar(
                pagamentoPixId,
                StatusProcessamentoPagamentoPix.ConsultaEmAndamento),
            StatusReconciliacaoPagamentoPix.EnvioEmAndamento => ResultadoProcessamentoPagamentoPix.Criar(
                pagamentoPixId,
                StatusProcessamentoPagamentoPix.EnvioEmAndamento),
            StatusReconciliacaoPagamentoPix.EnvioPendenteRecuperacao => ResultadoProcessamentoPagamentoPix.Criar(
                pagamentoPixId,
                StatusProcessamentoPagamentoPix.EnvioPendenteRecuperacao),
            StatusReconciliacaoPagamentoPix.NaoAplicavel => ResultadoProcessamentoPagamentoPix.Criar(
                pagamentoPixId,
                StatusProcessamentoPagamentoPix.NaoAplicavel),
            StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo or StatusReconciliacaoPagamentoPix.Consultado
                when EhConclusivo(reconciliacao.ResultadoOperacao) =>
                await AplicarUmaVezAsync(pagamentoPixId, cancellationToken),
            StatusReconciliacaoPagamentoPix.ResultadoJaConclusivo or StatusReconciliacaoPagamentoPix.Consultado =>
                ResultadoProcessamentoPagamentoPix.Criar(
                    pagamentoPixId,
                    StatusProcessamentoPagamentoPix.ReconciliacaoExecutadaAguardandoResultado),
            _ => throw new InvalidOperationException("O resultado da reconciliação Pix é inválido para processamento.")
        };
    }

    private async Task<ResultadoProcessamentoPagamentoPix> AplicarUmaVezAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken)
    {
        var aplicacao = await _aplicacaoResultadoService.AplicarAsync(pagamentoPixId, cancellationToken);
        return aplicacao.Status switch
        {
            StatusAplicacaoPagamentoPix.Aplicado or StatusAplicacaoPagamentoPix.JaAplicado =>
                MapearAplicacao(pagamentoPixId, aplicacao.Status),
            StatusAplicacaoPagamentoPix.SemResultadoConclusivo or StatusAplicacaoPagamentoPix.RequerReconciliacao =>
                ResultadoProcessamentoPagamentoPix.Criar(
                    pagamentoPixId,
                    StatusProcessamentoPagamentoPix.ReconciliacaoExecutadaAguardandoResultado),
            _ => throw new InvalidOperationException("O resultado da aplicação financeira é inválido para processamento.")
        };
    }

    private async Task<PagamentoPix> ObterPagamentoPixOuLancarExceptionAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken) =>
        await _pagamentoPixRepository.ObterPorIdAsync(pagamentoPixId, cancellationToken)
        ?? throw new PagamentoPixNaoEncontradoException();

    private static ResultadoProcessamentoPagamentoPix MapearAplicacao(
        Guid pagamentoPixId,
        StatusAplicacaoPagamentoPix status) =>
        ResultadoProcessamentoPagamentoPix.Criar(
            pagamentoPixId,
            status == StatusAplicacaoPagamentoPix.Aplicado
                ? StatusProcessamentoPagamentoPix.Aplicado
                : StatusProcessamentoPagamentoPix.JaAplicado);

    private static bool EhConclusivo(ResultadoOperacaoPagamentoPix? resultado) =>
        resultado is ResultadoOperacaoPagamentoPix.Confirmado or ResultadoOperacaoPagamentoPix.FalhaConfirmada;

    #endregion
}

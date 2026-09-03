using Application.Interfaces.Providers;
using Application.Interfaces.Services;
using Application.Models;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services;

/// <summary>
/// Consulta e registra evidências provider-agnostic sem liquidar o PagamentoPix
/// ou alterar o Cashback.
/// </summary>
public sealed class PagamentoPixReconciliacaoService : IPagamentoPixReconciliacaoService
{
    private readonly IPagamentoPixRepository _pagamentoPixRepository;
    private readonly IOperacaoPagamentoPixRepository _operacaoPagamentoPixRepository;
    private readonly IPixProvider _pixProvider;

    public PagamentoPixReconciliacaoService(
        IPagamentoPixRepository pagamentoPixRepository,
        IOperacaoPagamentoPixRepository operacaoPagamentoPixRepository,
        IPixProvider pixProvider)
    {
        _pagamentoPixRepository = pagamentoPixRepository;
        _operacaoPagamentoPixRepository = operacaoPagamentoPixRepository;
        _pixProvider = pixProvider;
    }

    #region Comandos

    public async Task<ResultadoReconciliacaoPagamentoPix> ReconciliarAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default)
    {
        if (pagamentoPixId == Guid.Empty)
            throw new ArgumentException("O identificador do Pagamento Pix é obrigatório.", nameof(pagamentoPixId));

        var pagamentoPix = await ObterPagamentoPixOuLancarExceptionAsync(pagamentoPixId, cancellationToken);
        if (pagamentoPix.Status != StatusPagamentoPix.Processando)
            return ResultadoReconciliacaoPagamentoPix.NaoAplicavel(pagamentoPixId);

        var historico = await _operacaoPagamentoPixRepository.ObterPorPagamentoPixIdAsync(
            pagamentoPixId,
            cancellationToken);
        ValidarHistoricoParaReconciliacao(historico);

        var conclusiva = ObterResultadoConclusivo(historico);
        if (conclusiva.HasValue)
            return ResultadoReconciliacaoPagamentoPix.JaConclusivo(pagamentoPixId, conclusiva.Value);

        var envioAberto = ObterEnvioAberto(historico);
        var consulta = OperacaoPagamentoPix.IniciarConsulta(pagamentoPixId);
        await _operacaoPagamentoPixRepository.AdicionarAsync(consulta, cancellationToken);

        var providerResult = await _pixProvider.ConsultarAsync(
            new PixConsultaRequest(pagamentoPix.Id),
            cancellationToken);
        var resultadoConsulta = MapearResultado(providerResult.Status);
        consulta.Finalizar(
            resultadoConsulta,
            providerResult.IdentificadorProvider,
            providerResult.Codigo);

        if (!await _operacaoPagamentoPixRepository.FinalizarAsync(consulta, CancellationToken.None))
        {
            throw new InvalidOperationException(
                "A resposta da consulta foi obtida, mas sua auditoria não pôde ser finalizada.");
        }

        var envioResolvido = false;
        if (EhConclusivo(resultadoConsulta) && envioAberto is not null)
        {
            envioAberto.Finalizar(
                resultadoConsulta,
                providerResult.IdentificadorProvider,
                providerResult.Codigo);
            envioResolvido = await FinalizarEnvioAbertoAsync(envioAberto, resultadoConsulta);
        }

        return ResultadoReconciliacaoPagamentoPix.Consultado(
            pagamentoPixId,
            consulta.Id,
            resultadoConsulta,
            envioResolvido);
    }

    #endregion

    #region Métodos Privados

    private async Task<PagamentoPix> ObterPagamentoPixOuLancarExceptionAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken) =>
        await _pagamentoPixRepository.ObterPorIdAsync(pagamentoPixId, cancellationToken)
        ?? throw new PagamentoPixNaoEncontradoException();

    private static void ValidarHistoricoParaReconciliacao(
        IReadOnlyCollection<OperacaoPagamentoPix> historico)
    {
        if (historico.Count == 0)
        {
            throw new InvalidOperationException(
                "Pagamento Pix Processando sem auditoria é inconsistente e requer intervenção técnica.");
        }

        var quantidadeEnviosAbertos = historico.Count(operacao =>
            operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio &&
            !operacao.FinishedAt.HasValue);
        if (quantidadeEnviosAbertos > 1)
        {
            throw new InvalidOperationException(
                "Pagamento Pix possui múltiplas operações de envio abertas e requer intervenção técnica.");
        }
    }

    private static ResultadoOperacaoPagamentoPix? ObterResultadoConclusivo(
        IReadOnlyCollection<OperacaoPagamentoPix> historico) =>
        historico
            .Where(operacao => EhConclusivo(operacao.Resultado))
            .OrderByDescending(operacao => operacao.FinishedAt)
            .ThenByDescending(operacao => operacao.CreatedAt)
            .Select(operacao => operacao.Resultado)
            .FirstOrDefault();

    private static OperacaoPagamentoPix? ObterEnvioAberto(
        IReadOnlyCollection<OperacaoPagamentoPix> historico) =>
        historico.SingleOrDefault(operacao =>
            operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio &&
            !operacao.FinishedAt.HasValue);

    private async Task<bool> FinalizarEnvioAbertoAsync(
        OperacaoPagamentoPix envioAberto,
        ResultadoOperacaoPagamentoPix resultadoConsulta)
    {
        if (await _operacaoPagamentoPixRepository.FinalizarAsync(envioAberto, CancellationToken.None))
            return true;

        var operacaoPersistida = await _operacaoPagamentoPixRepository.ObterPorIdAsync(
            envioAberto.Id,
            CancellationToken.None);
        if (operacaoPersistida?.Resultado == resultadoConsulta &&
            operacaoPersistida.FinishedAt.HasValue)
        {
            return false;
        }

        throw new InvalidOperationException(
            "A finalização concorrente da operação de envio é inconsistente e requer intervenção técnica.");
    }

    private static bool EhConclusivo(ResultadoOperacaoPagamentoPix? resultado) =>
        resultado is ResultadoOperacaoPagamentoPix.Confirmado or ResultadoOperacaoPagamentoPix.FalhaConfirmada;

    private static ResultadoOperacaoPagamentoPix MapearResultado(StatusPixProvider status) =>
        status switch
        {
            StatusPixProvider.Confirmado => ResultadoOperacaoPagamentoPix.Confirmado,
            StatusPixProvider.FalhaConfirmada => ResultadoOperacaoPagamentoPix.FalhaConfirmada,
            StatusPixProvider.Pendente => ResultadoOperacaoPagamentoPix.Pendente,
            StatusPixProvider.Indeterminado => ResultadoOperacaoPagamentoPix.Indeterminado,
            _ => throw new ArgumentOutOfRangeException(nameof(status), "O status do provider Pix é inválido.")
        };

    #endregion
}

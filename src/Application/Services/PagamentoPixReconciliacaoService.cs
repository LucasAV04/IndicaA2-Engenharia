using Application.Interfaces.Providers;
using Application.Interfaces.Services;
using Application.Interfaces.Stores;
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
    private readonly IPagamentoPixReconciliacaoStore _reconciliacaoStore;
    private readonly IPixProvider _pixProvider;

    public PagamentoPixReconciliacaoService(
        IPagamentoPixRepository pagamentoPixRepository,
        IOperacaoPagamentoPixRepository operacaoPagamentoPixRepository,
        IPagamentoPixReconciliacaoStore reconciliacaoStore,
        IPixProvider pixProvider)
    {
        _pagamentoPixRepository = pagamentoPixRepository;
        _operacaoPagamentoPixRepository = operacaoPagamentoPixRepository;
        _reconciliacaoStore = reconciliacaoStore;
        _pixProvider = pixProvider;
    }

    #region Comandos

    public async Task<ResultadoReconciliacaoPagamentoPix> ReconciliarAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default)
    {
        if (pagamentoPixId == Guid.Empty)
            throw new ArgumentException("O identificador do Pagamento Pix é obrigatório.", nameof(pagamentoPixId));

        await ObterPagamentoPixOuLancarExceptionAsync(pagamentoPixId, cancellationToken);
        var preparacao = await _reconciliacaoStore.PrepararConsultaAsync(pagamentoPixId, cancellationToken);
        if (preparacao.Status == StatusPreparacaoReconciliacaoPagamentoPix.NaoAplicavel)
            return ResultadoReconciliacaoPagamentoPix.NaoAplicavel(pagamentoPixId);

        if (preparacao.Status == StatusPreparacaoReconciliacaoPagamentoPix.ConsultaEmAndamento)
            return ResultadoReconciliacaoPagamentoPix.ConsultaEmAndamento(pagamentoPixId);

        if (preparacao.Status == StatusPreparacaoReconciliacaoPagamentoPix.ResultadoJaConclusivo)
        {
            return ResultadoReconciliacaoPagamentoPix.JaConclusivo(
                pagamentoPixId,
                preparacao.ResultadoOperacao
                ?? throw new InvalidOperationException("A preparação não informou a evidência conclusiva."),
                preparacao.OperacaoEnvioAbertaResolvida);
        }

        if (preparacao.Status != StatusPreparacaoReconciliacaoPagamentoPix.ConsultaPreparada ||
            !preparacao.OperacaoConsultaId.HasValue)
        {
            throw new InvalidOperationException("A preparação de reconciliação retornou um estado inválido.");
        }

        var consulta = await _operacaoPagamentoPixRepository.ObterPorIdAsync(
            preparacao.OperacaoConsultaId.Value,
            cancellationToken)
            ?? throw new InvalidOperationException("A auditoria de consulta preparada não foi encontrada.");

        var providerResult = await _pixProvider.ConsultarAsync(
            new PixConsultaRequest(pagamentoPixId),
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
        if (EhConclusivo(resultadoConsulta))
        {
            var pagamentoPix = await ObterPagamentoPixOuLancarExceptionAsync(pagamentoPixId, CancellationToken.None);
            var historico = await _operacaoPagamentoPixRepository.ObterPorPagamentoPixIdAsync(
                pagamentoPixId,
                CancellationToken.None);
            var cicloAtual = IdentificarCicloAtual(historico, pagamentoPix.QuantidadeTentativas);
            if (!cicloAtual.Envio.FinishedAt.HasValue)
            {
                cicloAtual.Envio.Finalizar(
                    resultadoConsulta,
                    providerResult.IdentificadorProvider,
                    providerResult.Codigo);
                envioResolvido = await FinalizarEnvioAbertoAsync(cicloAtual.Envio, resultadoConsulta);
            }
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

    private static CicloAtual IdentificarCicloAtual(
        IReadOnlyCollection<OperacaoPagamentoPix> historico,
        int tentativaAtual)
    {
        var enviosDaTentativaAtual = historico
            .Where(operacao =>
                operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio &&
                operacao.NumeroTentativaEnvio == tentativaAtual)
            .ToArray();
        if (enviosDaTentativaAtual.Length != 1)
        {
            throw new InvalidOperationException(
                "Pagamento Pix Processando deve possuir exatamente um envio para a tentativa atual.");
        }

        var envioAtual = enviosDaTentativaAtual[0];
        var envioAnteriorAberto = historico.Any(operacao =>
            operacao.TipoOperacao == TipoOperacaoPagamentoPix.Envio &&
            operacao.NumeroTentativaEnvio < tentativaAtual &&
            !operacao.FinishedAt.HasValue);
        if (envioAnteriorAberto)
        {
            throw new InvalidOperationException(
                "Pagamento Pix possui envio aberto de tentativa anterior e requer intervenção técnica.");
        }

        var consultasDoCicloAtual = historico
            .Where(operacao =>
                operacao.TipoOperacao == TipoOperacaoPagamentoPix.Consulta &&
                operacao.CreatedAt > envioAtual.CreatedAt)
            .ToArray();

        return new CicloAtual(envioAtual, consultasDoCicloAtual);
    }

    private static OperacaoPagamentoPix? ObterEvidenciaConclusiva(
        CicloAtual cicloAtual)
    {
        var evidenciasConclusivas = new[] { cicloAtual.Envio }
            .Concat(cicloAtual.Consultas)
            .Where(operacao => EhConclusivo(operacao.Resultado))
            .ToArray();
        var resultadosConclusivos = evidenciasConclusivas
            .Select(operacao => operacao.Resultado!.Value)
            .Distinct()
            .ToArray();
        if (resultadosConclusivos.Length > 1)
        {
            throw new InvalidOperationException(
                "Pagamento Pix possui evidências conclusivas conflitantes no ciclo da tentativa atual.");
        }

        return evidenciasConclusivas.Length == 0
            ? null
            : evidenciasConclusivas
                .OrderByDescending(operacao => operacao.FinishedAt)
                .ThenByDescending(operacao => operacao.CreatedAt)
                .First();
    }

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

    private sealed record CicloAtual(
        OperacaoPagamentoPix Envio,
        IReadOnlyCollection<OperacaoPagamentoPix> Consultas);

    #endregion
}

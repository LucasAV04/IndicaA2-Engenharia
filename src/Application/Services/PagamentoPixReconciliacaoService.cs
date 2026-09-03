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
        var cicloAtual = IdentificarCicloAtual(historico, pagamentoPix.QuantidadeTentativas);

        var conclusiva = ObterResultadoConclusivo(cicloAtual);
        if (conclusiva.HasValue)
            return ResultadoReconciliacaoPagamentoPix.JaConclusivo(pagamentoPixId, conclusiva.Value);

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
        if (EhConclusivo(resultadoConsulta) && !cicloAtual.Envio.FinishedAt.HasValue)
        {
            cicloAtual.Envio.Finalizar(
                resultadoConsulta,
                providerResult.IdentificadorProvider,
                providerResult.Codigo);
            envioResolvido = await FinalizarEnvioAbertoAsync(cicloAtual.Envio, resultadoConsulta);
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

    private static ResultadoOperacaoPagamentoPix? ObterResultadoConclusivo(
        CicloAtual cicloAtual)
    {
        var resultadosConclusivos = new[] { cicloAtual.Envio }
            .Concat(cicloAtual.Consultas)
            .Where(operacao => EhConclusivo(operacao.Resultado))
            .Select(operacao => operacao.Resultado!.Value)
            .Distinct()
            .ToArray();
        if (resultadosConclusivos.Length > 1)
        {
            throw new InvalidOperationException(
                "Pagamento Pix possui evidências conclusivas conflitantes no ciclo da tentativa atual.");
        }

        return resultadosConclusivos.Length == 0
            ? null
            : resultadosConclusivos[0];
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

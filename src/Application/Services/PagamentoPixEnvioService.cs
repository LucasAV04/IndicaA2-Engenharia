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
/// Orquestra o envio após a preparação atômica da ordem e de sua auditoria.
/// Não decide a liquidação financeira: PagamentoPix permanece Processando.
/// </summary>
public sealed class PagamentoPixEnvioService : IPagamentoPixEnvioService
{
    private readonly IPagamentoPixRepository _pagamentoPixRepository;
    private readonly IPagamentoPixEnvioStore _pagamentoPixEnvioStore;
    private readonly IOperacaoPagamentoPixRepository _operacaoPagamentoPixRepository;
    private readonly IPixProvider _pixProvider;

    public PagamentoPixEnvioService(
        IPagamentoPixRepository pagamentoPixRepository,
        IPagamentoPixEnvioStore pagamentoPixEnvioStore,
        IOperacaoPagamentoPixRepository operacaoPagamentoPixRepository,
        IPixProvider pixProvider)
    {
        _pagamentoPixRepository = pagamentoPixRepository;
        _pagamentoPixEnvioStore = pagamentoPixEnvioStore;
        _operacaoPagamentoPixRepository = operacaoPagamentoPixRepository;
        _pixProvider = pixProvider;
    }

    #region Comandos

    public async Task<ResultadoEnvioPagamentoPix> ProcessarEnvioAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken = default)
    {
        if (pagamentoPixId == Guid.Empty)
            throw new ArgumentException("O identificador do Pagamento Pix é obrigatório.", nameof(pagamentoPixId));

        cancellationToken.ThrowIfCancellationRequested();
        _ = await ObterPagamentoPixOuLancarExceptionAsync(pagamentoPixId, cancellationToken);

        var preparacao = await _pagamentoPixEnvioStore.TentarPrepararEnvioAsync(
            pagamentoPixId,
            cancellationToken);
        if (!preparacao.Adquirido)
            return ResultadoEnvioPagamentoPix.NaoAdquirido(pagamentoPixId);

        var operacaoId = preparacao.OperacaoPagamentoPixId!.Value;
        var tentativa = preparacao.NumeroTentativaEnvio!.Value;
        var pagamentoPix = await ObterPagamentoPixOuLancarExceptionAsync(pagamentoPixId, cancellationToken);
        var operacao = await _operacaoPagamentoPixRepository.ObterPorIdAsync(operacaoId, cancellationToken)
            ?? throw new InvalidOperationException("A operação de envio preparada não foi encontrada para auditoria.");

        ValidarPreparacaoPersistida(pagamentoPix, operacao, tentativa);

        var providerResult = await _pixProvider.EnviarAsync(
            new PixEnvioRequest(
                pagamentoPix.Id,
                pagamentoPix.Valor,
                pagamentoPix.TipoChavePix,
                pagamentoPix.ChavePix),
            cancellationToken);

        var resultadoOperacao = MapearResultado(providerResult.Status);
        operacao.Finalizar(
            resultadoOperacao,
            providerResult.IdentificadorProvider,
            providerResult.Codigo);

        if (!await _operacaoPagamentoPixRepository.FinalizarAsync(operacao, cancellationToken))
        {
            throw new InvalidOperationException(
                "A resposta do provider foi obtida, mas a finalização da auditoria não pôde ser persistida.");
        }

        return ResultadoEnvioPagamentoPix.Executado(
            pagamentoPixId,
            operacaoId,
            tentativa,
            resultadoOperacao);
    }

    #endregion

    #region Métodos Privados

    private async Task<PagamentoPix> ObterPagamentoPixOuLancarExceptionAsync(
        Guid pagamentoPixId,
        CancellationToken cancellationToken) =>
        await _pagamentoPixRepository.ObterPorIdAsync(pagamentoPixId, cancellationToken)
        ?? throw new PagamentoPixNaoEncontradoException();

    private static void ValidarPreparacaoPersistida(
        PagamentoPix pagamentoPix,
        OperacaoPagamentoPix operacao,
        int tentativa)
    {
        if (pagamentoPix.Status != StatusPagamentoPix.Processando ||
            pagamentoPix.QuantidadeTentativas != tentativa ||
            operacao.PagamentoPixId != pagamentoPix.Id ||
            operacao.TipoOperacao != TipoOperacaoPagamentoPix.Envio ||
            operacao.NumeroTentativaEnvio != tentativa ||
            operacao.FinishedAt.HasValue)
        {
            throw new InvalidOperationException(
                "A preparação persistida do envio Pix está inconsistente e requer reconciliação.");
        }
    }

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

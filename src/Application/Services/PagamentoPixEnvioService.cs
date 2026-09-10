using Application.Interfaces.Providers;
using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Application.Models;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using System.Runtime.ExceptionServices;

namespace Application.Services;

/// <summary>
/// Orquestra o envio após a preparação atômica da ordem e de sua auditoria.
/// Não decide a liquidação financeira: PagamentoPix permanece Processando.
/// </summary>
public sealed class PagamentoPixEnvioService : IPagamentoPixEnvioService
{
    private readonly IPagamentoPixRepository _pagamentoPixRepository;
    private readonly IPagamentoPixEnvioStore _pagamentoPixEnvioStore;
    private readonly IPixProvider _pixProvider;

    public PagamentoPixEnvioService(
        IPagamentoPixRepository pagamentoPixRepository,
        IPagamentoPixEnvioStore pagamentoPixEnvioStore,
        IPixProvider pixProvider)
    {
        _pagamentoPixRepository = pagamentoPixRepository;
        _pagamentoPixEnvioStore = pagamentoPixEnvioStore;
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
        var leaseId = preparacao.LeaseId!.Value;
        var pagamentoPix = await ObterPagamentoPixOuLancarExceptionAsync(pagamentoPixId, cancellationToken);
        ValidarPreparacaoPersistida(pagamentoPix, tentativa);

        PixProviderResult providerResult;
        try
        {
            providerResult = await _pixProvider.EnviarAsync(
                new PixEnvioRequest(
                    pagamentoPix.Id,
                    pagamentoPix.Valor,
                    pagamentoPix.TipoChavePix,
                    pagamentoPix.ChavePix),
                cancellationToken);
        }
        catch (Exception exception)
        {
            await RegistrarIndeterminadoOuLancarAsync(pagamentoPixId, operacaoId, leaseId, exception);
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

        var resultadoOperacao = MapearResultado(providerResult.Status);
        var finalizacao = await _pagamentoPixEnvioStore.FinalizarEnvioAsync(
            pagamentoPixId,
            operacaoId,
            leaseId,
            resultadoOperacao,
            providerResult.IdentificadorProvider,
            providerResult.Codigo,
            cancellationToken);
        if (!finalizacao.Finalizada)
            throw new InvalidOperationException("A resposta do provider foi obtida, mas o lease de envio não autorizou a finalização da auditoria.");

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

    private static void ValidarPreparacaoPersistida(PagamentoPix pagamentoPix, int tentativa)
    {
        if (pagamentoPix.Status != StatusPagamentoPix.Processando ||
            pagamentoPix.QuantidadeTentativas != tentativa)
        {
            throw new InvalidOperationException(
                "A preparação persistida do envio Pix está inconsistente e requer reconciliação.");
        }
    }

    private async Task RegistrarIndeterminadoOuLancarAsync(
        Guid pagamentoPixId,
        Guid operacaoEnvioId,
        Guid leaseId,
        Exception excecaoOriginal)
    {
        try
        {
            var finalizacao = await _pagamentoPixEnvioStore.FinalizarEnvioAsync(
                pagamentoPixId,
                operacaoEnvioId,
                leaseId,
                ResultadoOperacaoPagamentoPix.Indeterminado,
                null,
                null,
                CancellationToken.None);
            if (!finalizacao.Finalizada)
            {
                throw new InvalidOperationException(
                    "A falha do provider não pôde ser auditada porque o lease de envio não pertence mais ao executor.",
                    excecaoOriginal);
            }
        }
        catch (Exception exception) when (!ReferenceEquals(exception, excecaoOriginal))
        {
            throw new InvalidOperationException(
                "A falha do provider ocorreu e a persistência da auditoria de envio também falhou.",
                new AggregateException(excecaoOriginal, exception));
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

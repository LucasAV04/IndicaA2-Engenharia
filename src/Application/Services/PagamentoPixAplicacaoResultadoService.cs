using Application.Interfaces.Services;
using Application.Interfaces.Stores;
using Application.Models;
using Domain.Entities;
using Domain.Enums;
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
    private readonly IOperacaoPagamentoPixRepository _operacaoPagamentoPixRepository;
    private readonly IPagamentoPixAplicacaoResultadoStore _aplicacaoResultadoStore;

    public PagamentoPixAplicacaoResultadoService(
        IPagamentoPixRepository pagamentoPixRepository,
        ICashbackRepository cashbackRepository,
        IOperacaoPagamentoPixRepository operacaoPagamentoPixRepository,
        IPagamentoPixAplicacaoResultadoStore aplicacaoResultadoStore)
    {
        _pagamentoPixRepository = pagamentoPixRepository;
        _cashbackRepository = cashbackRepository;
        _operacaoPagamentoPixRepository = operacaoPagamentoPixRepository;
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

        var historico = await _operacaoPagamentoPixRepository.ObterPorPagamentoPixIdAsync(
            pagamentoPixId,
            cancellationToken);
        var cicloAtual = IdentificarCicloAtual(historico, pagamentoPix.QuantidadeTentativas);
        var resultadoConclusivo = ObterResultadoConclusivo(cicloAtual);
        if (!resultadoConclusivo.HasValue)
            return ResultadoAplicacaoPagamentoPix.SemResultadoConclusivo(pagamentoPixId);

        if (!cicloAtual.Envio.FinishedAt.HasValue)
            return ResultadoAplicacaoPagamentoPix.RequerReconciliacao(pagamentoPixId);

        if (EstadoJaAplicado(pagamentoPix, cashback, resultadoConclusivo.Value))
            return ResultadoAplicacaoPagamentoPix.JaAplicado(pagamentoPixId, resultadoConclusivo.Value);

        ValidarEstadoInicialParaAplicacao(pagamentoPix, cashback, resultadoConclusivo.Value);
        AplicarTransicoesDoDominio(pagamentoPix, cashback, resultadoConclusivo.Value);

        var resultadoPersistencia = await _aplicacaoResultadoStore.AplicarAsync(
            CriarRequest(pagamentoPix, cashback, resultadoConclusivo.Value),
            cancellationToken);

        return resultadoPersistencia == ResultadoPersistenciaAplicacaoPagamentoPix.JaAplicado
            ? ResultadoAplicacaoPagamentoPix.JaAplicado(pagamentoPixId, resultadoConclusivo.Value)
            : ResultadoAplicacaoPagamentoPix.Aplicado(pagamentoPixId, resultadoConclusivo.Value);
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
                "Pagamento Pix deve possuir exatamente um envio para a tentativa atual.");
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

    private static ResultadoOperacaoPagamentoPix? ObterResultadoConclusivo(CicloAtual cicloAtual)
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

        return resultadosConclusivos.Length == 0 ? null : resultadosConclusivos[0];
    }

    private static bool EstadoJaAplicado(
        PagamentoPix pagamentoPix,
        Cashback cashback,
        ResultadoOperacaoPagamentoPix resultadoConclusivo) =>
        resultadoConclusivo switch
        {
            ResultadoOperacaoPagamentoPix.Confirmado =>
                pagamentoPix.Status == StatusPagamentoPix.Concluido &&
                cashback.Status == StatusCashback.Pago,
            ResultadoOperacaoPagamentoPix.FalhaConfirmada =>
                (pagamentoPix.Status is StatusPagamentoPix.Falhou or StatusPagamentoPix.FalhaDefinitiva) &&
                cashback.Status == StatusCashback.Disponivel,
            _ => false
        };

    private static void ValidarEstadoInicialParaAplicacao(
        PagamentoPix pagamentoPix,
        Cashback cashback,
        ResultadoOperacaoPagamentoPix resultadoConclusivo)
    {
        if (pagamentoPix.Status != StatusPagamentoPix.Processando ||
            cashback.Status != StatusCashback.Disponivel)
        {
            throw new InvalidOperationException(
                $"O estado financeiro atual é incompatível com a evidência conclusiva '{resultadoConclusivo}'.");
        }
    }

    private static void AplicarTransicoesDoDominio(
        PagamentoPix pagamentoPix,
        Cashback cashback,
        ResultadoOperacaoPagamentoPix resultadoConclusivo)
    {
        if (resultadoConclusivo == ResultadoOperacaoPagamentoPix.Confirmado)
        {
            pagamentoPix.ConfirmarConclusao();
            cashback.RegistrarPagamento();
            return;
        }

        if (resultadoConclusivo == ResultadoOperacaoPagamentoPix.FalhaConfirmada)
        {
            pagamentoPix.RegistrarFalha();
            return;
        }

        throw new InvalidOperationException("Somente resultados conclusivos podem ser aplicados financeiramente.");
    }

    private static AplicacaoResultadoPagamentoPixRequest CriarRequest(
        PagamentoPix pagamentoPix,
        Cashback cashback,
        ResultadoOperacaoPagamentoPix resultadoConclusivo) =>
        new(
            pagamentoPix.Id,
            cashback.Id,
            pagamentoPix.UsuarioBeneficiarioId,
            cashback.UsuarioIndicadorId,
            pagamentoPix.Valor,
            pagamentoPix.QuantidadeTentativas,
            resultadoConclusivo,
            pagamentoPix.Status,
            cashback.Status,
            pagamentoPix.UpdatedAt,
            cashback.UpdatedAt);

    private static bool EhConclusivo(ResultadoOperacaoPagamentoPix? resultado) =>
        resultado is ResultadoOperacaoPagamentoPix.Confirmado or ResultadoOperacaoPagamentoPix.FalhaConfirmada;

    private sealed record CicloAtual(
        OperacaoPagamentoPix Envio,
        IReadOnlyCollection<OperacaoPagamentoPix> Consultas);

    #endregion
}

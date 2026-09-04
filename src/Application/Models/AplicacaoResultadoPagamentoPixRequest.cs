using Domain.Enums;

namespace Application.Models;

/// <summary>
/// Dados não sensíveis necessários à persistência coordenada do resultado financeiro.
/// </summary>
public sealed record AplicacaoResultadoPagamentoPixRequest(
    Guid PagamentoPixId,
    Guid CashbackId,
    Guid UsuarioBeneficiarioId,
    Guid UsuarioIndicadorId,
    decimal Valor,
    int QuantidadeTentativas,
    ResultadoOperacaoPagamentoPix ResultadoConclusivo,
    StatusPagamentoPix StatusPagamentoPixFinal,
    StatusCashback StatusCashbackFinal,
    DateTime PagamentoPixUpdatedAt,
    DateTime CashbackUpdatedAt);

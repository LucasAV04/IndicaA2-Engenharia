using Domain.Enums;

namespace Application.Models;

/// <summary>
/// Contrato seguro da orquestração interna. Não expõe chave Pix, payloads,
/// tokens de lease, credenciais ou detalhes do provider.
/// </summary>
public sealed record ResultadoProcessamentoPagamentoPix(
    Guid PagamentoPixId,
    StatusProcessamentoPagamentoPix Status,
    ResultadoOperacaoPagamentoPix? ResultadoOperacao)
{
    public static ResultadoProcessamentoPagamentoPix Criar(
        Guid pagamentoPixId,
        StatusProcessamentoPagamentoPix status,
        ResultadoOperacaoPagamentoPix? resultadoOperacao = null) =>
        new(pagamentoPixId, status, resultadoOperacao);
}

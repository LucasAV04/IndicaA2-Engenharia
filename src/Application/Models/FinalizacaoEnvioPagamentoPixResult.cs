namespace Application.Models;

/// <summary>
/// Resultado da finalização condicional da auditoria de envio pelo proprietário
/// atual do lease persistente.
/// </summary>
public sealed record FinalizacaoEnvioPagamentoPixResult(bool Finalizada);

namespace Domain.Exceptions;

public sealed class TransicaoPagamentoPixInvalidaException(string acao, string statusAtual) : DomainException(
    $"Não é possível {acao} o Pagamento Pix: status atual é '{statusAtual}'.");

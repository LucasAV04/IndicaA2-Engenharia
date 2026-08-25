namespace Domain.Exceptions;

public sealed class PagamentoPixNaoEncontradoException() : DomainException(
    "Pagamento Pix não encontrado.");

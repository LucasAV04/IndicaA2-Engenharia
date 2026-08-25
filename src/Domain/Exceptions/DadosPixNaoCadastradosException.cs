namespace Domain.Exceptions;

public sealed class DadosPixNaoCadastradosException() : DomainException(
    "O usuário beneficiário não possui Dados Pix cadastrados.");

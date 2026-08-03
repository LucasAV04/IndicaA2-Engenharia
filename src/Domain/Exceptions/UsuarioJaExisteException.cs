namespace Domain.Exceptions
{
    public sealed class UsuarioJaExisteException() : DomainException("Já existe um usuário cadastrado com este e-mail.");
}

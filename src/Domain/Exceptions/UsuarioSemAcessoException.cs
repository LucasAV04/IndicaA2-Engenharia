namespace Domain.Exceptions;

public sealed class UsuarioSemAcessoException : DomainException
{
    public UsuarioSemAcessoException()
        : base("Usuário sem permissão para acessar o sistema.")
    {
    }
}

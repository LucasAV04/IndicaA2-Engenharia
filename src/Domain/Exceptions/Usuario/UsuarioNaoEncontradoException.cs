namespace Domain.Exceptions.Usuario
{
    public sealed class UsuarioNaoEncontradoException:DomainException
    {
        public UsuarioNaoEncontradoException()
        : base("Usuário não encontrado.")
        {
        }   
    }
}
